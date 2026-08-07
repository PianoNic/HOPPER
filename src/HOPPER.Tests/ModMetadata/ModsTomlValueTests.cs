using HOPPER.Application.ModMetadata;

namespace HOPPER.Tests.ModMetadata
{
    public class ModsTomlValueTests
    {
        // Copied from a real Jade jar: CRLF endings, and logoFile sitting inside [[mods]] rather
        // than above it. Both are ordinary and neither was covered before.
        private const string Real =
            "modLoader = \"javafml\"\r\n"
            + "loaderVersion = \"[46,)\"\r\n"
            + "license = \"CC BY-NC-SA 4.0\"\r\n"
            + "\r\n"
            + "[[mods]]\r\n"
            + "modId = \"jade\"\r\n"
            + "version = \"${file.jarVersion}\"\r\n"
            + "displayName = \"Jade\"\r\n"
            + "logoFile = \"icon.png\"\r\n";

        [Test]
        public async Task Value_FindsAKeyInsideATableAndSurvivesCrlf()
        {
            await Assert.That(ModsTomlParser.Value(Real, "logoFile")).IsEqualTo("icon.png");
        }

        [Test]
        public async Task Value_FindsAKeyAboveTheFirstTable()
        {
            await Assert.That(ModsTomlParser.Value(Real, "modLoader")).IsEqualTo("javafml");
        }

        [Test]
        public async Task Value_IgnoresAKeyThatIsOnlyInAComment()
        {
            await Assert.That(ModsTomlParser.Value("# logoFile = \"nope.png\"\nmodId = \"x\"\n", "logoFile")).IsNull();
        }

        [Test]
        public async Task Value_IsNullWhenTheKeyIsAbsent()
        {
            await Assert.That(ModsTomlParser.Value(Real, "iconFile")).IsNull();
        }
    }
}
