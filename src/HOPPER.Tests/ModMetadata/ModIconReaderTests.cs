using System.IO.Compression;
using System.Text;
using HOPPER.Application.ModMetadata;

namespace HOPPER.Tests.ModMetadata
{
    public class ModIconReaderTests
    {
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        private static MemoryStream Jar(params (string Path, byte[] Bytes)[] entries)
        {
            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, bytes) in entries)
                {
                    using var stream = archive.CreateEntry(path).Open();
                    stream.Write(bytes);
                }
            }

            buffer.Position = 0;
            return buffer;
        }

        private static (string, byte[]) Text(string path, string content) =>
            (path, Encoding.UTF8.GetBytes(content));

        [Test]
        public async Task Reads_TheLogoAForgeJarDeclares()
        {
            using var jar = Jar(
                Text("META-INF/mods.toml", "modLoader=\"javafml\"\nlogoFile=\"icon.png\"\n[[mods]]\nmodId=\"jei\"\n"),
                ("icon.png", Png));

            await Assert.That(ModIconReader.Read(jar)).IsEquivalentTo(Png);
        }

        [Test]
        public async Task Reads_ALogoDeclaredTheWayForgeModsActuallyWriteIt()
        {
            using var jar = Jar(
                Text("META-INF/mods.toml", """
                    modLoader = "javafml"
                    loaderVersion = "[47,)"
                    license = "MIT"
                    logoFile = "icon.png"

                    [[mods]]
                    modId = "jade"
                    """),
                ("icon.png", Png));

            await Assert.That(ModIconReader.Read(jar)).IsEquivalentTo(Png);
        }

        [Test]
        public async Task Reads_TheIconAFabricJarDeclares()
        {
            using var jar = Jar(
                Text("fabric.mod.json", """{"id":"jei","icon":"assets/jei/icon.png"}"""),
                ("assets/jei/icon.png", Png));

            await Assert.That(ModIconReader.Read(jar)).IsEquivalentTo(Png);
        }

        [Test]
        public async Task Reads_TheBiggestOfASizeKeyedFabricIcon()
        {
            using var jar = Jar(
                Text("fabric.mod.json", """{"id":"jei","icon":{"32":"small.png","128":"big.png"}}"""),
                ("small.png", [0x89, 0x50, 0x4E, 0x47, 9]),
                ("big.png", Png));

            await Assert.That(ModIconReader.Read(jar)).IsEquivalentTo(Png);
        }

        [Test]
        public async Task Reads_TheIconQuiltHidesTwoLevelsDown()
        {
            using var jar = Jar(
                Text("quilt.mod.json", """{"quilt_loader":{"id":"jei","metadata":{"icon":"icon.png"}}}"""),
                ("icon.png", Png));

            await Assert.That(ModIconReader.Read(jar)).IsEquivalentTo(Png);
        }

        [Test]
        public async Task Returns_NothingWhenTheJarDeclaresNoIcon()
        {
            using var jar = Jar(Text("fabric.mod.json", """{"id":"jei"}"""));
            await Assert.That(ModIconReader.Read(jar)).IsNull();
        }

        [Test]
        public async Task Returns_NothingWhenTheDeclaredPathIsNotThere()
        {
            using var jar = Jar(Text("fabric.mod.json", """{"id":"jei","icon":"gone.png"}"""));
            await Assert.That(ModIconReader.Read(jar)).IsNull();
        }

        [Test]
        [Arguments("../../../etc/passwd")]
        [Arguments("C:/Windows/win.ini")]
        [Arguments("assets\\jei\\icon.png")]
        [Arguments("a/../../b.png")]
        [Arguments("")]
        [Arguments("   ")]
        public async Task Normalise_RefusesAPathThatEscapesTheArchive(string declared)
        {
            await Assert.That(ModIconReader.Normalise(declared)).IsNull();
        }

        [Test]
        public async Task Normalise_KeepsAnOrdinaryPathAndDropsALeadingSlashOnly()
        {
            await Assert.That(ModIconReader.Normalise("assets/jei/icon.png")).IsEqualTo("assets/jei/icon.png");
            await Assert.That(ModIconReader.Normalise("/icon.png")).IsEqualTo("icon.png");
            await Assert.That(ModIconReader.Normalise("/etc/passwd")).IsEqualTo("etc/passwd");
        }

        [Test]
        public async Task Returns_NothingWhenTheDeclaredFileIsNotAnImage()
        {
            using var jar = Jar(
                Text("fabric.mod.json", """{"id":"jei","icon":"icon.png"}"""),
                Text("icon.png", "PK not really a png"));

            await Assert.That(ModIconReader.Read(jar)).IsNull();
        }

        [Test]
        public async Task Returns_NothingWhenTheIconIsAbsurdlyLarge()
        {
            var huge = new byte[ModIconReader.MaxIconBytes + 1024];
            Png.CopyTo(huge, 0);

            using var jar = Jar(
                Text("fabric.mod.json", """{"id":"jei","icon":"icon.png"}"""),
                ("icon.png", huge));

            await Assert.That(ModIconReader.Read(jar)).IsNull();
        }

        [Test]
        public async Task Returns_NothingForSomethingThatIsNotAJarAtAll()
        {
            using var nonsense = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));
            await Assert.That(ModIconReader.Read(nonsense)).IsNull();
        }
    }
}
