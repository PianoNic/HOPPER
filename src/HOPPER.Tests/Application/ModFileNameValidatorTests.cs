using HOPPER.Application;

namespace HOPPER.Tests.Application
{
    public class ModFileNameValidatorTests
    {
        [Test]
        [Arguments("jei-1.20.1-15.2.0.27.jar")]
        [Arguments("Journeymap.JAR")]
        [Arguments("some mod with spaces.jar")]
        [Arguments("mod.with.dots.jar")]
        public async Task Validate_AcceptableName_ReturnsItUnchanged(string name)
        {
            await Assert.That(ModFileNameValidator.Validate(name)).IsEqualTo(name);
        }

        [Test]
        public async Task Validate_NameLongerThan255_IsRejected()
        {
            var name = new string('x', 252) + ".jar";

            await Assert.That(() => ModFileNameValidator.Validate(name)).Throws<ArgumentException>();
        }

        [Test]
        public async Task Validate_NameOfExactly255_IsAccepted()
        {
            var name = new string('x', 251) + ".jar";

            await Assert.That(ModFileNameValidator.Validate(name)).Length().IsEqualTo(255);
        }

        [Test]
        [Arguments("../../autostart/evil.jar")]
        [Arguments("sub/dir/mod.jar")]
        [Arguments("sub\\dir\\mod.jar")]
        [Arguments("..jar")]
        public async Task Validate_PathEscape_IsRejected(string name)
        {
            await Assert.That(() => ModFileNameValidator.Validate(name)).Throws<ArgumentException>();
        }

        [Test]
        [Arguments(".hidden.jar")]
        public async Task Validate_LeadingDot_IsRejected(string name)
        {
            await Assert.That(() => ModFileNameValidator.Validate(name)).Throws<ArgumentException>();
        }

        [Test]
        [Arguments("readme.txt")]
        [Arguments("mod.jar.disabled")]
        [Arguments("mod")]
        public async Task Validate_NotAJar_IsRejected(string name)
        {
            await Assert.That(() => ModFileNameValidator.Validate(name)).Throws<ArgumentException>();
        }

        [Test]
        [Arguments(null)]
        [Arguments("")]
        [Arguments("   ")]
        public async Task Validate_EmptyName_IsRejected(string? name)
        {
            await Assert.That(() => ModFileNameValidator.Validate(name)).Throws<ArgumentException>();
        }

        [Test]
        public async Task Validate_JarSuffix_IsCaseInsensitive()
        {
            await Assert.That(ModFileNameValidator.Validate("Mod.JaR")).IsEqualTo("Mod.JaR");
        }
    }
}
