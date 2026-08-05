using HOPPER.Application;

namespace HOPPER.Tests.Application
{
    /// <summary>
    /// The upload-time mirror of Syncer.sanitize() in the Java client. The two must agree: a name the
    /// client refuses is a jar that silently never installs, and the player sees a partial mod set
    /// with no error anywhere near the cause. The cases below are the client's rules, one for one.
    /// </summary>
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
        [Arguments("../../autostart/evil.jar")]
        [Arguments("sub/dir/mod.jar")]
        [Arguments("sub\\dir\\mod.jar")]
        [Arguments("..jar")]
        public async Task Validate_PathEscape_IsRejected(string name)
        {
            // Without this, a manifest entry could write outside hopper/ on every client at once.
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
            // Syncer.sanitize lowercases before the .jar check, so ".JAR" is legal there and must be
            // legal here too - rejecting it would block an upload the client would happily install.
            await Assert.That(ModFileNameValidator.Validate("Mod.JaR")).IsEqualTo("Mod.JaR");
        }
    }
}
