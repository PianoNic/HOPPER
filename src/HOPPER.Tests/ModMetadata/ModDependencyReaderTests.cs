using System.IO.Compression;
using System.Text;
using HOPPER.Application.ModMetadata;

namespace HOPPER.Tests.ModMetadata
{
    public class ModDependencyReaderTests
    {
        private static ZipArchive Jar(params (string Name, string Content)[] entries)
        {
            var buffer = new MemoryStream();

            using (var writing = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in entries)
                {
                    using var stream = writing.CreateEntry(name).Open();
                    stream.Write(Encoding.UTF8.GetBytes(content));
                }
            }

            buffer.Position = 0;
            return new ZipArchive(buffer, ZipArchiveMode.Read);
        }

        [Test]
        public async Task Fabric_ReadsTheDependsObjectKeys()
        {
            using var jar = Jar(("fabric.mod.json", """
                {"schemaVersion":1,"id":"entityculling",
                 "depends":{"fabric-api":"*","minecraft":">=1.20"}}
                """));

            await Assert.That(ModDependencyReader.Read(jar)).IsEquivalentTo(new[] { "fabric-api", "minecraft" });
        }

        [Test]
        public async Task Quilt_ReadsBareIdsAndObjectsAndSkipsOptionalOnes()
        {
            using var jar = Jar(("quilt.mod.json", """
                {"schema_version":1,"quilt_loader":{"id":"x","depends":[
                  "quilt_base",
                  {"id":"quilted_fabric_api","versions":"*"},
                  {"id":"sodium","optional":true}
                ]}}
                """));

            var read = ModDependencyReader.Read(jar);

            await Assert.That(read).Contains("quilt_base");
            await Assert.That(read).Contains("quilted_fabric_api");
            await Assert.That(read).DoesNotContain("sodium");
        }

        [Test]
        public async Task Quilt_TakesTheIdOutOfAGroupQualifiedDependency()
        {
            using var jar = Jar(("quilt.mod.json", """
                {"schema_version":1,"quilt_loader":{"id":"x","depends":[{"id":"org.quiltmc:quilt_base"}]}}
                """));

            await Assert.That(ModDependencyReader.Read(jar)).Contains("quilt_base");
        }

        [Test]
        public async Task Forge_ReadsMandatoryDependenciesAndSkipsTheOptionalOnes()
        {
            using var jar = Jar(("META-INF/mods.toml", """
                modLoader="javafml"
                [[mods]]
                modId="jei"
                [[dependencies.jei]]
                    modId="forge"
                    mandatory=true
                [[dependencies.jei]]
                    modId="patchouli"
                    mandatory=false
                [[dependencies.jei]]
                    modId="cloth-config"
                    mandatory=true
                """));

            var read = ModDependencyReader.Read(jar);

            await Assert.That(read).Contains("forge");
            await Assert.That(read).Contains("cloth-config");
            await Assert.That(read).DoesNotContain("patchouli");
        }

        [Test]
        public async Task Forge_TreatsADependencyWithNoMandatoryFlagAsRequired()
        {
            // Forge's own default is mandatory=true, so silence means required.
            using var jar = Jar(("META-INF/neoforge.mods.toml", """
                [[dependencies.x]]
                    modId="somelib"
                """));

            await Assert.That(ModDependencyReader.Read(jar)).Contains("somelib");
        }

        [Test]
        public async Task McmodInfo_ReadsRequiredModsAndStripsTheVersionRange()
        {
            using var jar = Jar(("mcmod.info", """
                [{"modid":"xaerominimap","requiredMods":["xaerolib@[1.7.0,)","forge"]}]
                """));

            await Assert.That(ModDependencyReader.Read(jar)).IsEquivalentTo(new[] { "xaerolib", "forge" });
        }

        [Test]
        public async Task McmodInfo_ReadsTheModListWrapperToo()
        {
            using var jar = Jar(("mcmod.info", """
                {"modListVersion":2,"modList":[{"modid":"a","requiredMods":["somelib"]}]}
                """));

            await Assert.That(ModDependencyReader.Read(jar)).Contains("somelib");
        }

        [Test]
        public async Task McmodInfo_LoadOrderIsNotARequirement()
        {
            // `dependencies` orders loading; it does not mean the mod cannot run without them.
            using var jar = Jar(("mcmod.info", """
                [{"modid":"a","dependencies":["jei"],"requiredMods":[]}]
                """));

            await Assert.That(ModDependencyReader.Read(jar)).IsEmpty();
        }

        [Test]
        public async Task AJarThatDeclaresNothing_HasNoDependencies()
        {
            using var jar = Jar(("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\n"));

            await Assert.That(ModDependencyReader.Read(jar)).IsEmpty();
        }

        [Test]
        [Arguments("minecraft")]
        [Arguments("java")]
        [Arguments("forge")]
        [Arguments("fabricloader")]
        [Arguments("quilt_loader")]
        public async Task WhatTheLoaderSuppliesIsNeverMissing(string id)
        {
            await Assert.That(ModDependencyReader.IsProvidedByTheLoader(id)).IsTrue();
        }

        [Test]
        public async Task AnOrdinaryModIsNotMistakenForSomethingTheLoaderSupplies()
        {
            await Assert.That(ModDependencyReader.IsProvidedByTheLoader("fabric-api")).IsFalse();
            await Assert.That(ModDependencyReader.IsProvidedByTheLoader("jei")).IsFalse();
        }
    }
}
