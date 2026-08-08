using HOPPER.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HOPPER.Infrastructure.Services
{
    public interface IBlobLockHold : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken = default);
    }

    public enum BlobSaveOutcome
    {
        Saved,
        Duplicate,
    }

    public static class BlobLock
    {
        private const int Namespace = 8421;

        // The ordering is the invariant: the bytes are only published once the row that references
        // them is committed, and only while nobody else can collect that hash.
        public static async Task<BlobSaveOutcome> SaveWithBlobAsync(
            HopperDbContext db, IBlobStorage blobs, StagedBlob staged, CancellationToken cancellationToken = default)
        {
            await using var hold = await HoldAsync(db, staged.Sha256, cancellationToken);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                return BlobSaveOutcome.Duplicate;
            }

            blobs.Promote(staged);
            await hold.CommitAsync(cancellationToken);

            return BlobSaveOutcome.Saved;
        }

        public static async Task<IBlobLockHold> HoldAsync(HopperDbContext db, string sha256, CancellationToken cancellationToken = default)
        {
            if (!db.Database.IsRelational())
                return NoHold.Instance;

            if (db.Database.CurrentTransaction is not null)
            {
                await Lock(db, sha256, cancellationToken);
                return NoHold.Instance;
            }

            var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                await Lock(db, sha256, cancellationToken);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }

            return new TransactionHold(transaction);
        }

        private static Task Lock(HopperDbContext db, string sha256, CancellationToken cancellationToken) =>
            db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock({Namespace}, hashtext({sha256}))", cancellationToken);

        private sealed class NoHold : IBlobLockHold
        {
            public static readonly NoHold Instance = new();

            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class TransactionHold(IDbContextTransaction transaction) : IBlobLockHold
        {
            private bool _committed;

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                await transaction.CommitAsync(cancellationToken);
                _committed = true;
            }

            public async ValueTask DisposeAsync()
            {
                if (!_committed)
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                await transaction.DisposeAsync();
            }
        }
    }
}
