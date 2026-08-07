using HOPPER.Infrastructure.Interfaces;

public static class TestBlobs
{
    public static async Task<(string Sha256, long Size)> StoreAsync(
        this IBlobStorage blobs, Stream content, long maxBytes, CancellationToken cancellationToken = default)
    {
        var staged = await blobs.StageAsync(content, maxBytes, cancellationToken);

        try
        {
            blobs.Promote(staged);
        }
        finally
        {
            blobs.Discard(staged);
        }

        return (staged.Sha256, staged.Size);
    }
}
