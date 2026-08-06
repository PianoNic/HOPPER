using System.IO.Compression;
using System.Text;
using HOPPER.Application.ModMetadata;

namespace HOPPER.Tests.ModMetadata
{
    /// <summary>
    /// Every fixture here is a REAL zip archive built in the test, because that is what the reader
    /// is handed in production and because "not a zip" is a case it has to survive rather than a
    /// case it never sees.
    ///
    /// This class has a one-for-one twin on the client side at
    /// src/HOPPER.Locator/hopper-core/src/test/java/ch/pianonic/hopper/ModIdsTest.java, with the
    /// same fixture text and the same expected output. The two implementations only do anything
    /// useful when they agree: the client migrates a jar out of the player's mods/ folder exactly
    /// when the id it read matches the id this side published. Change one, change both.
    ///
    /// The TOML content below is taken from real jars in a 102-mod Forge 1.20.1 instance -
    /// embeddium, Compat_FarmersDelight and yet_another_config_lib_v3 in particular - rather than
    /// invented, because the mistakes worth pinning are the ones real files actually make.
    /// </summary>
    public class ModIdReaderTests
    {
        private static byte[] Jar(params (string Name, string Content)[] entries)
        {
            var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in entries)
                {
                    using var stream = archive.CreateEntry(name).Open();
                    stream.Write(Encoding.UTF8.GetBytes(content));
                }
            }

            return buffer.ToArray();
        }

        private static string[] Read(params (string Name, string Content)[] entries) =>
            ModIdReader.Read(new MemoryStream(Jar(entries)));

        // ---------------------------------------------------------------- mods.toml

        private const string EmbeddiumToml = """
            modLoader="javafml"
            loaderVersion="[47,)"
            license="LGPL-3.0-only"
            [[mods]]
            modId="embeddium"
            version="0.3.31+mc1.20.1"
            displayName="Embeddium"
            logoFile="icon.png"
            description='''
            Embeddium is a fork of Rubidium, a fork of Sodium with patches for Forge
            '''
            credits="embeddedt, NanoLive, CaffeineMC"
            authors="embeddedt"

            [[mods]]
            modId = "rubidium"
            version = "0.7.1"
            displayName = "Rubidium (Embeddium)"
            description = '''
            Stub, to allow mods detecting Rubidium to function as expected.
            '''

            # Enforce new enough Oculus
            [[dependencies.embeddium]]
            modId = "oculus"
            mandatory = false
            versionRange = "(1.6.15,)"
            ordering = "BEFORE"
            side = "CLIENT"

            # The new config screen supersedes TexTrue's Embeddium Options
            [[dependencies.embeddium]]
            modId = "textrues_embeddium_options"
            mandatory = false
            versionRange = "[0.0.0-NOT-COMPATIBLE]"
            ordering = "BEFORE"
            side = "CLIENT"
            """;

        [Test]
        public async Task ModsToml_SingleModsBlock_ReturnsTheModId()
        {
            var ids = Read(("META-INF/mods.toml", """
                modLoader="javafml"
                loaderVersion="[47,)"
                [[mods]]
                modId="jei"
                version="15.3.0.4"
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "jei" });
        }

        [Test]
        public async Task ModsToml_TwoModsBlocks_ReturnsBothInFileOrder()
        {
            var ids = ModsTomlParser.Parse(EmbeddiumToml);

            await Assert.That(ids).IsEquivalentTo(new[] { "embeddium", "rubidium" });
        }

        [Test]
        public async Task ModsToml_DependencyTables_AreNeverMistakenForMods()
        {
            // The most important test in this file. 98 of the 104 real toml files in the reference
            // instance carry [[dependencies.<id>]] tables, each with its own modId key naming a
            // different mod. A naive grep for modId returns four ids here, and the two extras -
            // oculus and textrues_embeddium_options - are mods a player very likely has installed
            // for real. Getting this wrong does not fail safe: it makes the client move an unrelated
            // jar out of a folder HOPPER was told never to manage.
            var ids = ModsTomlParser.Parse(EmbeddiumToml);

            await Assert.That(ids).DoesNotContain("oculus");
            await Assert.That(ids).DoesNotContain("textrues_embeddium_options");
        }

        [Test]
        public async Task ModsToml_QuotedDependencyTableName_IsNotAModsTable()
        {
            // yet_another_config_lib_v3 writes its dependency headers with the table path itself
            // quoted, so the name has to be unquoted before it is compared to "mods".
            var ids = ModsTomlParser.Parse("""
                [[mods]]
                modId = "yet_another_config_lib_v3"

                [["dependencies.yet_another_config_lib_v3"]]
                modId = "minecraft"
                mandatory = true
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "yet_another_config_lib_v3" });
        }

        [Test]
        public async Task ModsToml_InlineModsArray_IsRead()
        {
            // Compat_FarmersDelight.jar verbatim. [[mods]] never appears; the lowcodefml datapack
            // toolchain writes the array inline. Note the mixed quoting inside one inline table -
            // the double quotes exist because the value contains an apostrophe.
            var ids = ModsTomlParser.Parse("""
                modLoader = 'lowcodefml'
                loaderVersion = '[40,)'
                license = 'LicenseRef-Unknown'
                showAsResourcePack = false
                mods = [
                	{ modId = 'farmersdelightcompat', version = '1.1.1', displayName = "Farmer's Delight Compat", description = "Remove duplicate items and blocks with the goal of integrating Farmer's Delight and other similar mods so they work as if they were a single mod!", logoFile = 'farmers-delight-compat_pack.png', updateJSONURL = 'https://api.modrinth.com/updates/bVyGNtz1/forge_updates.json', authors = 'Kanadeyoru', displayURL = 'https://modrinth.com/mod/farmers-delight-compat' },
                ]
                issueTrackerURL = 'https://github.com/FusionSwarly/Create-Structures/issues'
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "farmersdelightcompat" });
        }

        [Test]
        public async Task ModsToml_InlineModsArrayFollowedByDependencyTables_ReadsOnlyTheInlineIds()
        {
            // The create-structures shape: inline mods array, then real dependency tables after it.
            var ids = ModsTomlParser.Parse("""
                modLoader = 'lowcodefml'
                mods = [
                	{ modId = 'create_structures', version = '0.1.1' },
                ]

                [[dependencies.create_structures]]
                modId = "create"
                mandatory = true
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "create_structures" });
        }

        [Test]
        public async Task ModsToml_InlineModsArrayOnOneLine_IsRead()
        {
            var ids = ModsTomlParser.Parse("""
                mods = [ { modId = 'onelinemod', version = '1.0' } ]
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "onelinemod" });
        }

        [Test]
        public async Task ModsToml_MultilineStringContainingATableHeader_IsSkipped()
        {
            // A description is free text and "[[mods]]" or "# Features" inside one is entirely
            // ordinary. 67 of the 104 real files carry a triple-quoted string.
            var ids = ModsTomlParser.Parse("""
                [[mods]]
                modId = "realmod"
                description = '''
                # Features
                [[mods]]
                modId = "evil"
                '''
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "realmod" });
        }

        [Test]
        public async Task ModsToml_UnterminatedMultilineString_SwallowsTheRestOfTheFile()
        {
            // Parser state hygiene: an unbalanced ''' must not be treated as closed on the next
            // line, and must not leak structure out of the string body.
            var ids = ModsTomlParser.Parse("""
                [[mods]]
                modId = "realmod"
                description = '''
                never closed
                modId = "evil"
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "realmod" });
        }

        [Test]
        public async Task ModsToml_TrailingCommentAfterAQuotedValue_IsStripped()
        {
            // 43 of the 104 real files do this.
            var ids = ModsTomlParser.Parse("""
                [[mods]]
                modId = "commented" # this is the mod
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "commented" });
        }

        [Test]
        public async Task ModsToml_HashInsideAQuotedValue_IsNotAComment()
        {
            var ids = ModsTomlParser.Parse("""
                [[mods]]
                description = "sharp # sign"
                modId = "hashvalue"
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "hashvalue" });
        }

        [Test]
        public async Task ModsToml_HeaderWithATrailingComment_IsRecognised()
        {
            // 23 of the 104 real files put a comment on the header line itself.
            var ids = ModsTomlParser.Parse("""
                [[mods]] # the mod
                modId = "headercomment"
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "headercomment" });
        }

        [Test]
        public async Task ModsToml_SingleQuotedValue_IsRead()
        {
            var ids = ModsTomlParser.Parse("""
                [[mods]]
                modId = 'singlequoted'
                """);

            await Assert.That(ids).IsEquivalentTo(new[] { "singlequoted" });
        }

        [Test]
        public async Task ModsToml_NoWhitespaceAroundEquals_IsRead()
        {
            // 80 of the 104 real files write it this way.
            var ids = ModsTomlParser.Parse("[[mods]]\nmodId=\"tight\"\n");

            await Assert.That(ids).IsEquivalentTo(new[] { "tight" });
        }

        [Test]
        public async Task ModsToml_IndentedKeysAndTabs_AreRead()
        {
            // 65 of the 104 real files indent the keys under their header.
            var ids = ModsTomlParser.Parse("  [[mods]]\n\t  modId = \"indented\"\n");

            await Assert.That(ids).IsEquivalentTo(new[] { "indented" });
        }

        [Test]
        public async Task ModsToml_CrlfLineEndings_AreRead()
        {
            // 12 of the 104 real files are CRLF.
            var ids = ModsTomlParser.Parse("[[mods]]\r\nmodId = \"crlf\"\r\n");

            await Assert.That(ids).IsEquivalentTo(new[] { "crlf" });
        }

        [Test]
        [Arguments("Not An Id")]
        [Arguments("1starts-with-a-digit")]
        [Arguments("UPPERCASE")]
        [Arguments("x")]
        [Arguments("")]
        public async Task ModsToml_IdFailingTheForgeRegex_IsDropped(string candidate)
        {
            // ^[a-z][a-z0-9_.-]{1,63}$, taken verbatim from ModInfo.class. A cheap backstop against
            // a parser bug turning a fragment of a description into an "id" the client matches on.
            var ids = ModsTomlParser.Parse($"[[mods]]\nmodId = \"{candidate}\"\n");

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task ModsToml_LowercaseModidSpelling_IsNotRead()
        {
            // The toml key is modId, camelCase. "modid" is the mcmod.info spelling and appears in
            // zero of the 104 real toml files.
            var ids = ModsTomlParser.Parse("[[mods]]\nmodid = \"wrongcase\"\n");

            await Assert.That(ids).IsEmpty();
        }

        // ---------------------------------------------------------------- precedence

        [Test]
        public async Task NeoForgeToml_WinsOverModsToml_WhenBothArePresent()
        {
            // The two declare different ids so the precedence is observable. NeoForge 21.1+ reads
            // META-INF/neoforge.mods.toml and treats META-INF/mods.toml as a legacy Forge marker.
            var ids = Read(
                ("META-INF/mods.toml", "[[mods]]\nmodId = \"legacyid\"\n"),
                ("META-INF/neoforge.mods.toml", "[[mods]]\nmodId = \"modernid\"\n"));

            await Assert.That(ids).IsEquivalentTo(new[] { "modernid" });
        }

        [Test]
        public async Task NeoForgeToml_ThatYieldsNothing_FallsBackToModsToml()
        {
            // A broken new file must not cost us the ids the old one still carries.
            var ids = Read(
                ("META-INF/mods.toml", "[[mods]]\nmodId = \"stillhere\"\n"),
                ("META-INF/neoforge.mods.toml", "modLoader = \"javafml\"\n"));

            await Assert.That(ids).IsEquivalentTo(new[] { "stillhere" });
        }

        [Test]
        public async Task BothTomlsDeclaringTheSameId_ReturnItOnce()
        {
            // All three jars in the reference instance that ship both declare identical ids.
            var ids = Read(
                ("META-INF/mods.toml", "[[mods]]\nmodId = \"terralith\"\n"),
                ("META-INF/neoforge.mods.toml", "[[mods]]\nmodId = \"terralith\"\n"));

            await Assert.That(ids).IsEquivalentTo(new[] { "terralith" });
        }

        // ---------------------------------------------------------------- fabric

        [Test]
        public async Task FabricJson_TopLevelId_IsRead()
        {
            var ids = Read(("fabric.mod.json", """
                {
                    "schemaVersion": 1,
                    "id": "terralith",
                    "version": "2.5.4",
                    "name": "Terralith"
                }
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "terralith" });
        }

        [Test]
        public async Task FabricJson_DependsKeys_AreNotRead()
        {
            // "depends" is an object whose KEYS are mod ids. A recursive hunt for anything called
            // id would not find them, but a hunt for "any key that looks like an id" would - and
            // fabricloader and minecraft are on every install in the world.
            var ids = Read(("fabric.mod.json", """
                {
                    "schemaVersion": 1,
                    "id": "terralith",
                    "depends": { "fabricloader": ">=0.12.7", "fabric-api-base": "*", "minecraft": ">=1.20" }
                }
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "terralith" });
        }

        // ---------------------------------------------------------------- quilt

        [Test]
        public async Task QuiltJson_NestedLoaderId_IsRead()
        {
            var ids = Read(("quilt.mod.json", """
                {
                    "schema_version": 1,
                    "quilt_loader": {
                        "group": "net.stardustlabs",
                        "id": "terralith",
                        "version": "2.5.4"
                    }
                }
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "terralith" });
        }

        [Test]
        public async Task QuiltJson_DependsArrayIds_AreNotRead()
        {
            // Terralith verbatim. Quilt's depends is an array of objects each carrying an id key -
            // exactly the same hazard as [[dependencies.*]] in toml. Reading the exact path is what
            // keeps minecraft and quilt_resource_loader out.
            var ids = Read(("quilt.mod.json", """
                {
                    "schema_version": 1,
                    "quilt_loader": {
                        "id": "terralith",
                        "version": "2.5.4",
                        "depends": [
                            { "id": "minecraft", "versions": ">=1.20" },
                            { "id": "quilt_resource_loader", "versions": "*", "unless": "fabric-resource-loader-v0" }
                        ]
                    }
                }
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "terralith" });
        }

        [Test]
        public async Task QuiltJson_ProvidesArray_IsIgnored()
        {
            // provides is an aliasing mechanism ("this mod also satisfies X"), not an identity.
            // Treating it as one would migrate every jar that declares the same alias.
            var ids = Read(("quilt.mod.json", """
                {
                    "quilt_loader": {
                        "id": "realid",
                        "provides": [ { "id": "aliasid" } ]
                    }
                }
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "realid" });
        }

        [Test]
        public async Task QuiltJson_TopLevelIdWithoutQuiltLoader_IsNotRead()
        {
            // Wrong depth. Fabric reads $.id, Quilt reads $.quilt_loader.id, and a file that only
            // has the outer shape is not a quilt.mod.json this reader understands.
            var ids = ModIdReader.FromQuiltJson("""{ "id": "wrongdepth" }""");

            await Assert.That(ids).IsEmpty();
        }

        // ---------------------------------------------------------------- mcmod.info

        [Test]
        public async Task McmodInfo_RootArray_IsRead()
        {
            // The Forge 1.12.2 universal jar's own mcpmod.info, byte-identical in format.
            var ids = Read(("mcmod.info", """
                [
                {
                  "modid": "mcp",
                  "name": "Minecraft Coder Pack",
                  "version": "9.42",
                  "mcversion": "1.12.2",
                  "authors": ["Searge", "ProfMobius"],
                  "dependencies": []
                }
                ]
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "mcp" });
        }

        [Test]
        public async Task McmodInfo_ModListWrapper_IsRead()
        {
            // MetadataCollection.from branches on isJsonArray, so both shapes are valid input.
            var ids = Read(("mcmod.info", """
                { "modListVersion": 2, "modList": [ { "modid": "examplemod", "name": "Example", "version": "1.0" } ] }
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "examplemod" });
        }

        [Test]
        public async Task McmodInfo_SeveralEntries_ReturnsAll()
        {
            // The "parent" field exists precisely so one file can list a mod and its children.
            var ids = Read(("mcmod.info", """
                [
                  { "modid": "parentmod", "name": "Parent" },
                  { "modid": "childmod", "name": "Child", "parent": "parentmod" }
                ]
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "parentmod", "childmod" });
        }

        [Test]
        public async Task McmodInfo_CamelCaseModId_IsNotRead()
        {
            // This format alone is lowercase. ModMetadata.class's Java field is modId but it carries
            // @SerializedName("modid"), and getting this backwards is the single most likely mistake
            // because it is the opposite convention from mods.toml.
            var ids = ModIdReader.FromMcmodInfo("""[ { "modId": "wrongcase" } ]""");

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task McmodInfo_DependenciesAndRequiredMods_AreIgnored()
        {
            var ids = Read(("mcmod.info", """
                [
                  {
                    "modid": "themod",
                    "dependencies": ["forge", "jei"],
                    "requiredMods": ["Forge@[14.23,)"],
                    "authorList": ["someone"]
                  }
                ]
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "themod" });
        }

        // ---------------------------------------------------------------- union and jar-in-jar

        [Test]
        public async Task JarWithTomlAndFabricAndQuilt_AllDeclaringOneId_ReturnsThatIdOnce()
        {
            // Terralith is the one jar in the reference instance that ships all three.
            var ids = Read(
                ("META-INF/mods.toml", "[[mods]]\nmodId = \"terralith\"\n"),
                ("fabric.mod.json", """{ "schemaVersion": 1, "id": "terralith" }"""),
                ("quilt.mod.json", """{ "quilt_loader": { "id": "terralith" } }"""));

            await Assert.That(ids).IsEquivalentTo(new[] { "terralith" });
        }

        [Test]
        public async Task JarDeclaringDifferentIdsInDifferentFormats_ReturnsTheUnion()
        {
            var ids = Read(
                ("META-INF/mods.toml", "[[mods]]\nmodId = \"forgeside\"\n"),
                ("fabric.mod.json", """{ "id": "fabricside" }"""));

            await Assert.That(ids).IsEquivalentTo(new[] { "forgeside", "fabricside" });
        }

        [Test]
        public async Task NestedJarModIds_AreNotRead()
        {
            // DO NOT "FIX" THIS BY ADDING RECURSION.
            //
            // Fourteen different top-level mods in the 102-jar reference instance bundle a nested
            // copy of mixinextras. If HOPPER read nested jars, one distributed jar containing
            // mixinextras would publish "mixinextras" as a manifest mod id, and the client would
            // then see thirteen unrelated jars in the player's mods/ folder as the same mod and
            // start moving them into hoppermods/replaced/. That is data movement against jars
            // HOPPER was told never to touch.
            //
            // It is also unnecessary: jar-in-jar exists so nested copies do not collide, and the
            // loader version-selects them. The hard duplicate-mod failure is between top-level
            // files only.
            var nested = Jar(("META-INF/mods.toml", "[[mods]]\nmodId = \"mixinextras\"\n"));

            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var stream = archive.CreateEntry("META-INF/jarjar/mixinextras-0.2.0.jar").Open())
                    stream.Write(nested);

                using (var stream = archive.CreateEntry("META-INF/jarjar/metadata.json").Open())
                    stream.Write(Encoding.UTF8.GetBytes("""{"jars":[]}"""));
            }

            var ids = ModIdReader.Read(new MemoryStream(buffer.ToArray()));

            await Assert.That(ids).IsEmpty();
        }

        // ---------------------------------------------------------------- never throws

        [Test]
        public async Task JarWithNoMetadata_ReturnsEmpty()
        {
            // A coremod or a plain library. Legitimate and extremely common.
            var ids = Read(("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\n"), ("com/example/Thing.class", "not really"));

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task NotAZipAtAll_ReturnsEmpty()
        {
            // The exact bytes the API contract tests store as a jar.
            var ids = ModIdReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("PK pretend forge jar payload")));

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task EmptyStream_ReturnsEmpty()
        {
            await Assert.That(ModIdReader.Read(new MemoryStream())).IsEmpty();
        }

        [Test]
        public async Task TruncatedZip_ReturnsEmpty()
        {
            var full = Jar(("META-INF/mods.toml", "[[mods]]\nmodId = \"truncated\"\n"));
            var half = full[..(full.Length / 2)];

            await Assert.That(ModIdReader.Read(new MemoryStream(half))).IsEmpty();
        }

        [Test]
        public async Task RandomBytes_ReturnEmptyRatherThanThrow()
        {
            var noise = new byte[4096];
            new Random(1234).NextBytes(noise);

            await Assert.That(ModIdReader.Read(new MemoryStream(noise))).IsEmpty();
        }

        [Test]
        public async Task MetadataEntryOverTheSizeCap_ReturnsEmpty()
        {
            // A real mods.toml is about a kilobyte. A jar is untrusted input.
            var padding = new string('x', 2 * 1024 * 1024);
            var ids = Read(("META-INF/mods.toml", $"[[mods]]\nmodId = \"toobig\"\ndescription = \"{padding}\"\n"));

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task MalformedJson_ReturnsEmpty()
        {
            // A trailing comma. This is parsed with JsonDocument's DEFAULT options on purpose: the
            // Java client reads the same file through ch.pianonic.hopper.Json, which is strict for
            // the manifest's sake, and two sides with different leniency would derive different id
            // sets from one jar. The Java twin of this test is malformedJsonYieldsNoIds.
            var ids = Read(("fabric.mod.json", """{ "id": "trailing", }"""));

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task JsonThatIsNotAnObject_ReturnsEmpty()
        {
            await Assert.That(ModIdReader.FromFabricJson("\"just a string\"")).IsEmpty();
            await Assert.That(ModIdReader.FromQuiltJson("[]")).IsEmpty();
            await Assert.That(ModIdReader.FromMcmodInfo("42")).IsEmpty();
        }

        [Test]
        public async Task Utf8BomInFrontOfTheMetadata_IsStripped()
        {
            // Real files carry one, and a BOM is a parse error for System.Text.Json.
            var ids = Read(("fabric.mod.json", "﻿{ \"id\": \"bommed\" }"));

            await Assert.That(ids).IsEquivalentTo(new[] { "bommed" });
        }

        [Test]
        public async Task EntryNamesAreMatchedCaseSensitively()
        {
            // All five names are exact and case-sensitive in every loader checked, so a loose match
            // could only ever invent one.
            var ids = Read(("META-INF/Mods.toml", "[[mods]]\nmodId = \"wrongcase\"\n"));

            await Assert.That(ids).IsEmpty();
        }
    }
}
