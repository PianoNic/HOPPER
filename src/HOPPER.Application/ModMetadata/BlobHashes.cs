using System.Security.Cryptography;
using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.Application.ModMetadata
{
    public static class BlobHashes
    {
        public static string? Sha512(IBlobStorage blobs, string sha256)
        {
            try
            {
                using var stream = blobs.OpenRead(sha256);
                if (stream is null)
                    return null;

                using var algorithm = SHA512.Create();
                return Convert.ToHexStringLower(algorithm.ComputeHash(stream));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
