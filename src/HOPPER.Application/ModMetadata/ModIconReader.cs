using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace HOPPER.Application.ModMetadata
{
    public static class ModIconReader
    {
        public const int MaxIconBytes = 1024 * 1024;

        private const int MaxMetadataBytes = 512 * 1024;

        private static readonly byte[][] Signatures =
        [
            [0x89, 0x50, 0x4E, 0x47],
            [0xFF, 0xD8, 0xFF],
            [0x47, 0x49, 0x46, 0x38],
        ];

        public static byte[]? Read(Stream seekableJar)
        {
            try
            {
                using var archive = new ZipArchive(seekableJar, ZipArchiveMode.Read, leaveOpen: true);
                return Read(archive);
            }
            catch (Exception ex) when (ex is InvalidDataException
                                          or IOException
                                          or NotSupportedException
                                          or ObjectDisposedException
                                          or ArgumentException)
            {
                return null;
            }
        }

        public static byte[]? Read(ZipArchive archive)
        {
            foreach (var path in DeclaredPaths(archive))
            {
                var bytes = Extract(archive, path);
                if (bytes is not null) return bytes;
            }

            return null;
        }

        public static IEnumerable<string> DeclaredPaths(ZipArchive archive)
        {
            var toml = Text(archive, "META-INF/neoforge.mods.toml") ?? Text(archive, "META-INF/mods.toml");
            if (toml is not null)
            {
                var logo = ModsTomlParser.Value(toml, "logoFile");
                if (logo is not null) yield return logo;
            }

            var fabric = Text(archive, "fabric.mod.json");
            if (fabric is not null)
            {
                foreach (var path in FromFabricJson(fabric)) yield return path;
            }

            var quilt = Text(archive, "quilt.mod.json");
            if (quilt is not null)
            {
                foreach (var path in FromQuiltJson(quilt)) yield return path;
            }
        }

        public static string[] FromFabricJson(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                return IconPaths(doc.RootElement, "icon");
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public static string[] FromQuiltJson(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);

                if (!doc.RootElement.TryGetProperty("quilt_loader", out var loader)
                    || !loader.TryGetProperty("metadata", out var metadata))
                {
                    return [];
                }

                return IconPaths(metadata, "icon");
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static string[] IconPaths(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var icon)) return [];

            if (icon.ValueKind == JsonValueKind.String)
                return icon.GetString() is { Length: > 0 } one ? [one] : [];

            if (icon.ValueKind != JsonValueKind.Object) return [];

            return icon.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()?.Length > 0)
                .OrderByDescending(p => int.TryParse(p.Name, out var size) ? size : 0)
                .Select(p => p.Value.GetString()!)
                .ToArray();
        }

        private static byte[]? Extract(ZipArchive archive, string declared)
        {
            var path = Normalise(declared);
            if (path is null) return null;

            var entry = archive.GetEntry(path);
            if (entry is null || entry.Length > MaxIconBytes) return null;

            try
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();

                var chunk = new byte[8192];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > MaxIconBytes) return null;
                    buffer.Write(chunk, 0, read);
                }

                var bytes = buffer.ToArray();
                return LooksLikeAnImage(bytes) ? bytes : null;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
            {
                return null;
            }
        }

        public static string? Normalise(string? declared)
        {
            if (string.IsNullOrWhiteSpace(declared)) return null;

            var path = declared.Trim().TrimStart('/');
            if (path.Length == 0 || path.Length > 512) return null;

            if (path.Contains('\\', StringComparison.Ordinal)) return null;
            if (path.Contains(':', StringComparison.Ordinal)) return null;

            foreach (var segment in path.Split('/'))
            {
                if (segment == ".." || segment == ".") return null;
            }

            return path;
        }

        public static bool LooksLikeAnImage(byte[] bytes) =>
            Signatures.Any(signature =>
                bytes.Length >= signature.Length && bytes.AsSpan(0, signature.Length).SequenceEqual(signature));

        private static string? Text(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name);
            if (entry is null || entry.Length > MaxMetadataBytes) return null;

            try
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();

                var chunk = new byte[8192];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > MaxMetadataBytes) return null;
                    buffer.Write(chunk, 0, read);
                }

                var bytes = buffer.ToArray();
                var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
            {
                return null;
            }
        }
    }
}
