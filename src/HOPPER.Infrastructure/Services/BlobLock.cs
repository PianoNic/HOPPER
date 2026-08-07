using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HOPPER.Infrastructure.Services
{
    public interface IBlobLockHold : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken = default);
    }

    public static class BlobLock
    {
        private const int Namespace = 8421;

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
