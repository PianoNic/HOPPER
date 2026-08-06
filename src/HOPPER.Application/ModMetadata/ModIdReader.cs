using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.Application.ModMetadata
{
    /// <summary>Reads the mod ids a jar declares for itself, in every metadata format HOPPER's
    /// client adapters cover.
    ///
    /// This exists because filename and hash matching cannot solve the problem HOPPER actually has.
    /// A player who already carries jei-1.20.1-15.2.0.27.jar and a server that distributes
    /// jei-1.20.1-15.3.0.4.jar have two different filenames and two different hashes for one mod,
    /// and the loader refuses to start when it finds the id twice. Only the mod id identifies it, so
    /// the server publishes what it read here and the client matches on it.
    ///
    /// Every method is total: a stream that is not a zip, a truncated zip, a metadata file that does
    /// not parse and a jar that simply declares nothing all come out as an empty set. That is
    /// deliberate and it is the right bias - a missed id is a migration that does not happen, which
    /// is the crash the player already had, while a WRONG id is HOPPER moving a jar it was told never
    /// to touch. The test suite also stores payloads that are not zips at all.
    ///
    /// The JSON here is parsed with JsonDocument's DEFAULT options - no trailing commas, no
    /// comments. That is stricter than the Gson the loaders use, and it is deliberate: the Java
    /// client reads these same files through ch.pianonic.hopper.Json, which is strict by design
    /// because it also parses the manifest. Two sides with different leniency would derive different
    /// id sets from one jar, which is the exact failure this whole feature exists to avoid. Both
    /// sides are strict, so a hand-edited fabric.mod.json with a stray comma yields no ids on both,
    /// which is a missed migration and never a wrong move.</summary>
    public static class ModIdReader
    {
        /// <summary>Real mods.toml files are about a kilobyte. Anything past this is not metadata,
        /// and a jar is untrusted input.</summary>
        private const int MaxMetadataBytes = 1024 * 1024;

        private const string NeoForgeToml = "META-INF/neoforge.mods.toml";
        private const string ForgeToml = "META-INF/mods.toml";
        private const string FabricJson = "fabric.mod.json";
        private const string QuiltJson = "quilt.mod.json";
        private const string McmodInfo = "mcmod.info";

        /// <summary>Forge's own rule, taken verbatim from the regex and the message
        /// "Invalid modId found in file {} - {} does not match the standard: {}" inside
        /// net/neoforged/fml/loading/moddiscovery/ModInfo.class.
        ///
        /// Applied to every id from every format, including the Fabric and Quilt ones whose loaders
        /// document a slightly wider rule (3 to 64 characters, a leading digit permitted). Using one
        /// rule everywhere costs a sub-one-percent Fabric id and buys the guarantee that the Java
        /// side, which applies the same single regex, can never disagree with this one. A dropped id
        /// is a migration that does not happen; a disagreement is a jar moved for no reason.</summary>
        private static readonly Regex ValidModId = new("^[a-z][a-z0-9_.-]{1,63}$", RegexOptions.CultureInvariant);

        public static bool IsValidModId(string id) => ValidModId.IsMatch(id);

        /// <summary>Reads a jar's own declared mod ids.
        ///
        /// The stream must be SEEKABLE: ZipArchive reads the central directory at the end of the
        /// file. That is why extraction happens after the blob is stored rather than before - see
        /// <see cref="FromBlob"/>.</summary>
        public static string[] Read(Stream seekableJar)
        {
            try
            {
                using var archive = new ZipArchive(seekableJar, ZipArchiveMode.Read, leaveOpen: true);

                var ids = new List<string>();

                // Precedence applies only inside the toml pair, and only in this direction.
                // NeoForge 21.1+ reads META-INF/neoforge.mods.toml and treats META-INF/mods.toml as
                // the marker of a legacy Forge jar (JarModsDotTomlModFileReader names the new file;
                // the old one appears only in IncompatibleModReason's other-loader list). The
                // fallback when the new file is present but yields nothing is for the malformed
                // case: a broken new file must not cost us the ids the old one still carries.
                var toml = Text(archive, NeoForgeToml);
                var tomlIds = toml is null ? [] : ModsTomlParser.Parse(toml);

                if (tomlIds.Length == 0)
                {
                    var legacy = Text(archive, ForgeToml);
                    if (legacy is not null)
                        tomlIds = ModsTomlParser.Parse(legacy);
                }

                Add(ids, tomlIds);

                // Everything else is a union rather than a first match. Terralith ships mods.toml,
                // fabric.mod.json and quilt.mod.json, all three declaring "terralith", and the union
                // is one id. A jar whose formats genuinely disagreed would load under two ids
                // depending on the loader, so migrating on either is the safe answer.
                var fabric = Text(archive, FabricJson);
                if (fabric is not null) Add(ids, FromFabricJson(fabric));

                var quilt = Text(archive, QuiltJson);
                if (quilt is not null) Add(ids, FromQuiltJson(quilt));

                var mcmod = Text(archive, McmodInfo);
                if (mcmod is not null) Add(ids, FromMcmodInfo(mcmod));

                // Nested jars under META-INF/jarjar/ and META-INF/jars/ are DELIBERATELY not read,
                // and this is not an oversight to be fixed.
                //
                // In the 102-jar reference instance, 26 jars bundle nested jars and fourteen
                // different top-level mods bundle a copy of mixinextras. If HOPPER recursed, one
                // distributed jar containing mixinextras would publish "mixinextras" as a manifest
                // mod id, and the client would then see thirteen unrelated jars in the player's
                // mods/ folder as "the same mod" and start moving them into hoppermods/replaced/.
                // That is data movement against jars HOPPER was told never to touch.
                //
                // It is also unnecessary. Jar-in-jar exists precisely so nested copies do not
                // collide - the loader version-selects them. The hard "Found duplicate mods:"
                // failure this whole feature prevents is between top-level mod files, so top-level
                // ids are exactly the right scope. A pure container jar records nothing.
                return [.. ids];
            }
            catch (Exception ex) when (ex is InvalidDataException
                                          or IOException
                                          or NotSupportedException
                                          or ObjectDisposedException
                                          or ArgumentException)
            {
                return [];
            }
        }

        /// <summary>The uniform hook for every path that stores a jar.
        ///
        /// IBlobStorage.SaveAsync consumes its stream forward-only, and two of the four store paths
        /// hand it a stream that cannot seek - a ZipArchiveEntry inside an uploaded batch, and
        /// Modrinth's HTTP body. Reading before the save is therefore impossible uniformly. Reading
        /// after it, by content address, works identically everywhere and needs no interface change:
        /// OpenRead returns a FileStream, which is exactly what ZipArchive wants.
        ///
        /// Null means "we could not look", which is the retry signal ModIdBackfillService acts on.
        /// An empty array means "we looked and it declares none", which is final.</summary>
        public static string[]? FromBlob(IBlobStorage blobs, string sha256)
        {
            Stream? stream;

            try
            {
                stream = blobs.OpenRead(sha256);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }

            if (stream is null)
                return null;

            using (stream)
                return Read(stream);
        }

        /// <summary>Fabric's id is a top-level string under the key "id". Read by its exact path and
        /// never searched for: "depends" is an object whose KEYS are ids (fabricloader, minecraft,
        /// fabric-api-base), so a recursive hunt for anything called id returns the mod's
        /// dependencies as if they were the mod.</summary>
        public static string[] FromFabricJson(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.String)
                {
                    return [];
                }

                return One(id.GetString());
            }
            catch (JsonException)
            {
                return [];
            }
        }

        /// <summary>Quilt nests its id one level down, at quilt_loader.id. Its "depends" is an array
        /// of objects each carrying an id - the same hazard as [[dependencies.*]] in toml - so this
        /// too reads the exact path.
        ///
        /// quilt_loader.provides is ignored on purpose. It is an aliasing mechanism ("this mod also
        /// satisfies X"), not an identity, and treating it as one would migrate jars that merely
        /// declare the same alias.</summary>
        public static string[] FromQuiltJson(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("quilt_loader", out var loader)
                    || loader.ValueKind != JsonValueKind.Object
                    || !loader.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.String)
                {
                    return [];
                }

                return One(id.GetString());
            }
            catch (JsonException)
            {
                return [];
            }
        }

        /// <summary>Forge 1.12.2 and older. The key is "modid", ALL LOWERCASE - the opposite
        /// convention from mods.toml's camelCase modId, and the single most likely thing to get
        /// wrong. It is pinned by ModMetadata.class, whose Java field modId carries
        /// @SerializedName("modid").
        ///
        /// The root is a JSON array, or an object whose "modList" is one. Forge itself branches on
        /// exactly that (MetadataCollection.from calls isJsonArray and takes one of two paths), and
        /// an mcmod.info may legitimately list several mods - the "parent" field exists for child
        /// mods - so every element contributes.</summary>
        public static string[] FromMcmodInfo(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;

                JsonElement list;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    list = root;
                }
                else if (root.ValueKind == JsonValueKind.Object
                         && root.TryGetProperty("modList", out var wrapped)
                         && wrapped.ValueKind == JsonValueKind.Array)
                {
                    list = wrapped;
                }
                else
                {
                    return [];
                }

                var ids = new List<string>();

                foreach (var element in list.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object
                        && element.TryGetProperty("modid", out var id)
                        && id.ValueKind == JsonValueKind.String)
                    {
                        Add(ids, One(id.GetString()));
                    }
                }

                return [.. ids];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        /// <summary>Reads one entry by its EXACT, case-sensitive name. All five names are exact and
        /// case-sensitive in every loader checked, so enumerating and comparing loosely would only
        /// invent matches. Null when absent or over the size cap.</summary>
        private static string? Text(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name);
            if (entry is null)
                return null;

            // The declared length is a hint from the central directory and can lie, so the read
            // below is capped independently.
            if (entry.Length > MaxMetadataBytes)
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

            var bytes = buffer.ToArray();

            // A BOM in front of a JSON document is a parse error for System.Text.Json and a stray
            // character in front of a TOML key. Real files carry them.
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }

        private static string[] One(string? id) =>
            id is not null && IsValidModId(id) ? [id] : [];

        private static void Add(List<string> ids, string[] more)
        {
            foreach (var id in more)
            {
                if (!ids.Contains(id, StringComparer.Ordinal))
                    ids.Add(id);
            }
        }
    }
}
