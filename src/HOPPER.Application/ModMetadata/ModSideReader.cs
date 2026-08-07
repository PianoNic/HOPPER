using System.IO.Compression;
using System.Text.Json;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.Application.ModMetadata
{
    public static class ModSideReader
    {
        private const long MaxMetadataBytes = 1024 * 1024;

        public static ModSide FromStaged(IBlobStorage blobs, StagedBlob staged)
        {
            try
            {
                using var stream = blobs.OpenStaged(staged);
                return Read(stream);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or InvalidDataException)
            {
                return ModSide.Both;
            }
        }

        public static ModSide Read(ZipArchive archive)
        {
            try
            {
                var fabric = Text(archive, "fabric.mod.json");
                if (fabric is not null)
                    return FromFabricEnvironment(fabric);

                var quilt = Text(archive, "quilt.mod.json");
                if (quilt is not null)
                    return FromQuiltEnvironment(quilt);

                return ModSide.Both;
            }
            catch (Exception ex) when (ex is InvalidDataException
                                          or IOException
                                          or NotSupportedException
                                          or ObjectDisposedException
                                          or ArgumentException)
            {
                return ModSide.Both;
            }
        }

        public static ModSide Read(Stream jar)
        {
            try
            {
                using var archive = new ZipArchive(jar, ZipArchiveMode.Read, leaveOpen: true);
                return Read(archive);
            }
            catch (InvalidDataException)
            {
                return ModSide.Both;
            }
        }

        public static ModSide FromFabricEnvironment(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return Map(String(document.RootElement, "environment"));
            }
            catch (JsonException)
            {
                return ModSide.Both;
            }
        }

        public static ModSide FromQuiltEnvironment(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("quilt_loader", out var loader)
                    && loader.ValueKind == JsonValueKind.Object
                    && loader.TryGetProperty("minecraft", out var minecraft)
                    && minecraft.ValueKind == JsonValueKind.Object)
                {
                    return Map(String(minecraft, "environment"));
                }

                return ModSide.Both;
            }
            catch (JsonException)
            {
                return ModSide.Both;
            }
        }

        private static ModSide Map(string? environment) => environment?.Trim().ToLowerInvariant() switch
        {
            "client" => ModSide.ClientOnly,
            "server" => ModSide.ServerOnly,
            _ => ModSide.Both,
        };

        private static string? String(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string? Text(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name);
            if (entry is null || entry.Length > MaxMetadataBytes)
                return null;

            using var stream = entry.Open();
            using var buffer = new MemoryStream();

            var chunk = new byte[8192];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaxMetadataBytes)
                    return null;

                buffer.Write(chunk, 0, read);
            }

            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
