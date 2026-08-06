using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Exports
{
    /// <summary>Everything the three exporters do identically: check the server is described well
    /// enough to export, read its mods, build a zip on disk rather than in memory, and copy blob bytes
    /// into it.
    ///
    /// The one rule they all share and that this class enforces at the only place bytes are written:
    /// an entry path is always "&lt;prefix&gt;/" plus a filename that has already passed
    /// ModFileNameValidator, so nothing containing a separator, a "..", or a leading dot can reach the
    /// archive. Nothing an admin typed becomes a path.</summary>
    public abstract class PackExporterBase(HopperDbContext db, IBlobStorage blobs, IConfiguration configuration)
    {
        /// <summary>Written into every format's own version field. UTC, and sortable, because it is
        /// the only thing distinguishing two exports of the same server.</summary>
        protected static string PackVersion(DateTime utcNow) => utcNow.ToString("yyyy.MM.dd-HHmm");

        protected static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            // No naming policy, deliberately. Every property carries an explicit [JsonPropertyName]
            // because these are file-format contracts, and a policy here would silently override
            // them - "projectID" would become "projectId" and the manifest would stop being read.
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

        /// <summary>&lt;slug&gt;-&lt;yyyyMMdd-HHmmss&gt;.&lt;ext&gt;, UTC. The slug is already
        /// constrained to lowercase alphanumerics and dashes, so it is filename-safe by
        /// construction.</summary>
        protected static string FileNameFor(ExportContext context, string extension) =>
            $"{context.Server.Slug}-{context.UtcNow:yyyyMMdd-HHmmss}.{extension}";

        /// <summary>Opens a scratch file that deletes itself when the response finishes. It lives
        /// under the blob directory rather than the system temp dir so a deployment that gave HOPPER a
        /// large volume for jars does not export onto a small one.</summary>
        protected FileStream CreateScratchFile()
        {
            var root = configuration["Blobs:Directory"] is { Length: > 0 } configured
                ? configured
                : Path.Combine(AppContext.BaseDirectory, "blobs");

            var directory = Path.Combine(root, "exports");
            Directory.CreateDirectory(directory);

            return new FileStream(
                Path.Combine(directory, $"{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }

        /// <summary>Copies one mod's bytes into the archive under "&lt;prefix&gt;/&lt;filename&gt;".
        ///
        /// A mod whose blob has gone missing is named in a warning and skipped rather than failing the
        /// whole export: an admin with 200 mods and one broken blob wants 199 mods and a note, not a
        /// 500 and no pack.</summary>
        protected void WriteBlobEntry(ZipArchive archive, string prefix, Mod mod, List<string> warnings)
        {
            using var source = blobs.OpenRead(mod.Sha256);
            if (source is null)
            {
                warnings.Add($"{mod.FileName} was left out: its stored file is missing.");
                return;
            }

            // Validated rather than trusted, even though upload validated it too. This is the only
            // place a database value becomes a path inside an archive, so it is the right place for
            // the assertion to live.
            var fileName = ModFileNameValidator.Validate(mod.FileName);

            var entry = archive.CreateEntry($"{prefix}/{fileName}", CompressionLevel.Fastest);
            using var target = entry.Open();
            source.CopyTo(target);
        }

        protected static void WriteTextEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();

            // UTF-8 with no BOM and LF endings: a BOM in front of "[General]" makes the first INI key
            // unreadable, and a manifest is JSON, where a BOM is not permitted either.
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.NewLine = "\n";
            writer.Write(content);
        }

        protected static void WriteJsonEntry<T>(ZipArchive archive, string path, T value) =>
            WriteTextEntry(archive, path, JsonSerializer.Serialize(value, Json));

        /// <summary>Rewinds the finished archive and hands it back as the response body.</summary>
        protected static PackExportResult Finish(
            FileStream scratch, string fileName, string contentType, List<string> warnings)
        {
            scratch.Position = 0;
            return new PackExportResult(fileName, contentType, scratch, warnings);
        }
    }
}
