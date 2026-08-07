using System.IO.Compression;
using System.Text;
using HOPPER.Infrastructure.Services;

namespace HOPPER.Application.Imports
{
    public static class ZipEntryText
    {
        public static string? Read(ZipArchive archive, string name, long maxBytes) =>
            archive.GetEntry(name) is { } entry ? Read(entry, maxBytes) : null;

        public static string? Read(ZipArchiveEntry entry, long maxBytes)
        {
            if (entry.Length > maxBytes)
                return null;

            using var buffer = new MemoryStream();

            try
            {
                using var content = entry.Open();
                new LimitedStream(content, maxBytes, entry.Name).CopyTo(buffer);
            }
            catch (ContentTooLargeException)
            {
                return null;
            }

            var bytes = buffer.ToArray();
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }
    }
}
