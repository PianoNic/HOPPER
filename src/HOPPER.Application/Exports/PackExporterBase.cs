using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Exports
{
    public abstract class PackExporterBase(HopperDbContext db, IBlobStorage blobs, IConfiguration configuration)
    {
        protected static string PackVersion(DateTime utcNow) => utcNow.ToString("yyyy.MM.dd-HHmm");

        protected static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,

            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        protected sealed record ExportContext(
            Server Server,
            IReadOnlyList<Mod> Mods,
            string MinecraftVersion,
            ModLoader Loader,
            string LoaderVersion,
            DateTime UtcNow);

        protected async Task<ExportContext> LoadAsync(Guid serverId, CancellationToken cancellationToken)
        {
            var server = await db.Servers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == serverId, cancellationToken)
                ?? throw new ServerNotFoundException(serverId);

            var (minecraftVersion, loader, loaderVersion) = ServerPlatform.RequireForExport(server);

            var mods = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == serverId)
                .OrderBy(m => m.FileName)
                .ToListAsync(cancellationToken);

            return new ExportContext(server, mods, minecraftVersion, loader, loaderVersion, DateTime.UtcNow);
        }

        protected static string FileNameFor(ExportContext context, string extension) =>
            $"{context.Server.Slug}-{context.UtcNow:yyyyMMdd-HHmmss}.{extension}";

        protected FileStream CreateScratchFile()
        {
            var directory = BlobPaths.Exports(configuration);
            Directory.CreateDirectory(directory);

            return new FileStream(
                Path.Combine(directory, $"{Guid.NewGuid():N}{BlobPaths.ExportScratchExtension}"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }

        protected void WriteBlobEntry(ZipArchive archive, string prefix, Mod mod, List<string> warnings)
        {
            using var source = blobs.OpenRead(mod.Sha256);
            if (source is null)
            {
                warnings.Add($"{mod.FileName} was left out: its stored file is missing.");
                return;
            }

            var fileName = ModFileNameValidator.Validate(mod.FileName);

            var entry = archive.CreateEntry($"{prefix}/{fileName}", CompressionLevel.Fastest);
            using var target = entry.Open();
            source.CopyTo(target);
        }

        protected static void WriteTextEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();

            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.NewLine = "\n";
            writer.Write(content);
        }

        protected static void WriteJsonEntry<T>(ZipArchive archive, string path, T value) =>
            WriteTextEntry(archive, path, JsonSerializer.Serialize(value, Json));

        protected static PackExportResult Finish(
            FileStream scratch, string fileName, string contentType, List<string> warnings)
        {
            scratch.Position = 0;
            return new PackExportResult(fileName, contentType, scratch, warnings);
        }
    }
}
