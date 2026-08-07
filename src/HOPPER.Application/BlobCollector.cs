using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application
{
    public static class BlobCollector
    {
        public static async Task<bool> CollectAsync(
            HopperDbContext db, IBlobStorage blobs, string sha256, CancellationToken cancellationToken = default)
        {
            await using var hold = await BlobLock.HoldAsync(db, sha256, cancellationToken);

            if (await db.Mods.AnyAsync(m => m.Sha256 == sha256, cancellationToken))
                return false;

            blobs.Delete(sha256);
            await hold.CommitAsync(cancellationToken);
            return true;
        }
    }
}
