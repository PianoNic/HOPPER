using System.IO.Compression;
using System.Text;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Infrastructure.Services
{
    /// <summary>Copies the template jar and writes one entry into the copy. That entry is what makes a
    /// downloaded jar work with zero configuration: HopperLocator reads /hopper-server.properties out
    /// of its own classpath before it looks at config/hopper.properties, so the URL and token travel
    /// inside the file the player drops in mods/.</summary>
    public class LocatorJarBuilder(IConfiguration configuration) : ILocatorJarBuilder
    {
        /// <summary>Archive-root entry name, no leading slash. The Java side reads it as
        /// getResourceAsStream("/hopper-server.properties"), which resolves to exactly this.</summary>
        public const string ConfigEntry = "hopper-server.properties";

        /// <summary>A jar without this entry does not register the locator with Forge, so it would
        /// install cleanly and then do nothing at all. Cheapest possible check that the configured
        /// path really is a HOPPER locator and not some other jar that happens to be lying there.</summary>
        private const string ServiceEntry = "META-INF/services/net.minecraftforge.forgespi.locating.IModLocator";

        public byte[] Build(Guid serverId, string manifestUrl, string token)
        {
            var template = ResolveTemplatePath();

            if (!File.Exists(template))
                throw new LocatorTemplateMissingException(template);

            // The whole archive is held in memory: the template is tens of kilobytes, and building it
            // here rather than streaming means a failure part-way through can never have reached the
            // client as a half-patched jar.
            var buffer = new MemoryStream();

            try
            {
                using var source = ZipFile.OpenRead(template);

                if (source.GetEntry(ServiceEntry) is null)
                    throw new LocatorTemplateMissingException(template, "is not a HOPPER locator jar");

                // REWRITTEN, not updated in place, and this is not a style choice.
                //
                // ZipArchiveMode.Update on a jar produced by the JDK's `jar` tool silently corrupts
                // it. `jar` writes its entries with general-purpose flag bit 3 set (0x0808), which
                // puts each entry's sizes in a trailing 16-byte data descriptor rather than in the
                // local header. .NET's updater drops those descriptors when it rewrites but does not
                // account for them in the central directory it emits, so every entry after the first
                // compressed one is recorded 16 bytes further along than it actually is. .NET's own
                // reader is lenient enough to still find them; java.util.zip is not, and answers
                // "invalid LOC header (bad signature)" for exactly the entries that matter - the
                // class files and the service registration. Forge does not report that as a broken
                // jar. It skips the file, the locator never runs, and the player gets a vanilla game
                // with nothing in the log pointing anywhere near here.
                //
                // Copying every entry into a fresh archive means every header is one .NET wrote in
                // one pass, so the offsets cannot disagree with anything.
                using (var target = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var entry in source.Entries)
                    {
                        // Skipped rather than copied: a zip may legally hold two entries with the same
                        // name, and Java's class loader hands out whichever it meets first - which on
                        // a jar patched twice would be the stale token.
                        if (string.Equals(entry.FullName, ConfigEntry, StringComparison.Ordinal))
                            continue;

                        // A directory entry is a zero-length name ending in '/'. Deflating nothing
                        // costs two bytes and buys nothing, and stored is what every other zip writer
                        // produces for these.
                        var isDirectory = entry.FullName.EndsWith('/');

                        var copy = target.CreateEntry(
                            entry.FullName,
                            isDirectory ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                        copy.LastWriteTime = entry.LastWriteTime;

                        if (isDirectory)
                            continue;

                        using var from = entry.Open();
                        using var to = copy.Open();
                        from.CopyTo(to);
                    }

                    var config = target.CreateEntry(ConfigEntry, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(config.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                    {
                        // java.util.Properties splits on \n; a BOM or \r\n would ride into the first
                        // key's name and the value would silently never be found.
                        NewLine = "\n",
                    };

                    writer.WriteLine("# Generated by HOPPER. Do not edit - download a fresh jar instead.");
                    writer.WriteLine($"serverId={serverId}");
                    writer.WriteLine($"manifestUrl={manifestUrl}");
                    writer.WriteLine($"token={token}");

                    // Exactly three keys, all ASCII by construction (a GUID, an http(s) URL, 64 hex
                    // characters), so Properties' ISO-8859-1 load needs no escaping. `enabled` is
                    // deliberately absent: it stays the player's on-disk kill switch, and a jar that
                    // set it would take that away.
                }
            }
            catch (InvalidDataException ex)
            {
                throw new LocatorTemplateMissingException(template, "is not a readable zip archive", ex);
            }

            return buffer.ToArray();
        }

        private string ResolveTemplatePath()
        {
            var configured = configuration["Hopper:LocatorTemplatePath"];

            // Relative to the process, not to the caller's working directory: the same configured
            // value has to resolve identically whether HOPPER was started by `dotnet run`, by systemd
            // or by Docker's entrypoint.
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "locator", "hopper.jar")
                : Path.GetFullPath(configured, AppContext.BaseDirectory);
        }
    }
}
