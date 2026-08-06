using System.IO.Compression;
using System.Text;
using HOPPER.Application.ModMetadata;

namespace HOPPER.Tests.ModMetadata
{
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
            var ids = ModsTomlParser.Parse(EmbeddiumToml);

            await Assert.That(ids).DoesNotContain("oculus");
            await Assert.That(ids).DoesNotContain("textrues_embeddium_options");
        }

        [Test]
        public async Task ModsToml_QuotedDependencyTableName_IsNotAModsTable()
        {
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
            var ids = ModsTomlParser.Parse("[[mods]]\nmodId=\"tight\"\n");

            await Assert.That(ids).IsEquivalentTo(new[] { "tight" });
        }

        [Test]
        public async Task ModsToml_IndentedKeysAndTabs_AreRead()
        {
            var ids = ModsTomlParser.Parse("  [[mods]]\n\t  modId = \"indented\"\n");

            await Assert.That(ids).IsEquivalentTo(new[] { "indented" });
        }

        [Test]
        public async Task ModsToml_CrlfLineEndings_AreRead()
        {
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
            var ids = ModsTomlParser.Parse($"[[mods]]\nmodId = \"{candidate}\"\n");

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task ModsToml_LowercaseModidSpelling_IsNotRead()
        {
            var ids = ModsTomlParser.Parse("[[mods]]\nmodid = \"wrongcase\"\n");

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task NeoForgeToml_WinsOverModsToml_WhenBothArePresent()
        {
            var ids = Read(
                ("META-INF/mods.toml", "[[mods]]\nmodId = \"legacyid\"\n"),
                ("META-INF/neoforge.mods.toml", "[[mods]]\nmodId = \"modernid\"\n"));

            await Assert.That(ids).IsEquivalentTo(new[] { "modernid" });
        }

        [Test]
        public async Task NeoForgeToml_ThatYieldsNothing_FallsBackToModsToml()
        {
            var ids = Read(
                ("META-INF/mods.toml", "[[mods]]\nmodId = \"stillhere\"\n"),
                ("META-INF/neoforge.mods.toml", "modLoader = \"javafml\"\n"));

            await Assert.That(ids).IsEquivalentTo(new[] { "stillhere" });
        }

        [Test]
        public async Task BothTomlsDeclaringTheSameId_ReturnItOnce()
        {
            var ids = Read(
                ("META-INF/mods.toml", "[[mods]]\nmodId = \"terralith\"\n"),
                ("META-INF/neoforge.mods.toml", "[[mods]]\nmodId = \"terralith\"\n"));

            await Assert.That(ids).IsEquivalentTo(new[] { "terralith" });
        }

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
            var ids = Read(("fabric.mod.json", """
                {
                    "schemaVersion": 1,
                    "id": "terralith",
                    "depends": { "fabricloader": ">=0.12.7", "fabric-api-base": "*", "minecraft": ">=1.20" }
                }
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "terralith" });
        }

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
            var ids = ModIdReader.FromQuiltJson("""{ "id": "wrongdepth" }""");

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task McmodInfo_RootArray_IsRead()
        {
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
            var ids = Read(("mcmod.info", """
                { "modListVersion": 2, "modList": [ { "modid": "examplemod", "name": "Example", "version": "1.0" } ] }
                """));

            await Assert.That(ids).IsEquivalentTo(new[] { "examplemod" });
        }

        [Test]
        public async Task McmodInfo_SeveralEntries_ReturnsAll()
        {
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

        [Test]
        public async Task JarWithTomlAndFabricAndQuilt_AllDeclaringOneId_ReturnsThatIdOnce()
        {
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

        [Test]
        public async Task JarWithNoMetadata_ReturnsEmpty()
        {
            var ids = Read(("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\n"), ("com/example/Thing.class", "not really"));

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task NotAZipAtAll_ReturnsEmpty()
        {
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
            var padding = new string('x', 2 * 1024 * 1024);
            var ids = Read(("META-INF/mods.toml", $"[[mods]]\nmodId = \"toobig\"\ndescription = \"{padding}\"\n"));

            await Assert.That(ids).IsEmpty();
        }

        [Test]
        public async Task MalformedJson_ReturnsEmpty()
        {
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
            var ids = Read(("fabric.mod.json", "﻿{ \"id\": \"bommed\" }"));

            await Assert.That(ids).IsEquivalentTo(new[] { "bommed" });
        }

        [Test]
        public async Task EntryNamesAreMatchedCaseSensitively()
        {
            var ids = Read(("META-INF/Mods.toml", "[[mods]]\nmodId = \"wrongcase\"\n"));

            await Assert.That(ids).IsEmpty();
        }
    }
}
