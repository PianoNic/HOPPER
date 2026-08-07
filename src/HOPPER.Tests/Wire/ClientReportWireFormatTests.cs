using System.Text.Json;
using HOPPER.Application.Dtos.Clients;
using HOPPER.Domain.Enums;

namespace HOPPER.Tests.Wire
{
    public class ClientReportWireFormatTests
    {
        private const string ReportWithNullUsername = """
            {"clientId":"3f0f1f4a-4b3f-4d1e-9c0a-2b6d5e8f7a11","username":null,"mods":[{"file":"jei-1.20.1-15.2.0.27.jar","sha256":"817c44afc9bd5ffa653785e54107b708af0a1b5b695095e0a5235cfe7b24b4f3"}]}
            """;

        private const string ReportWithUsername = """
            {"clientId":"3f0f1f4a-4b3f-4d1e-9c0a-2b6d5e8f7a11","username":"Alex","mods":[{"file":"jei-1.20.1-15.2.0.27.jar","sha256":"817c44afc9bd5ffa653785e54107b708af0a1b5b695095e0a5235cfe7b24b4f3"}]}
            """;

        [Test]
        public async Task Deserialize_NullUsername_IsAccepted()
        {
            var dto = JsonSerializer.Deserialize<ClientReportDto>(ReportWithNullUsername);

            await Assert.That(dto).IsNotNull();
            await Assert.That(dto!.Username).IsNull();
            await Assert.That(dto.ClientId).IsEqualTo("3f0f1f4a-4b3f-4d1e-9c0a-2b6d5e8f7a11");
            await Assert.That(dto.Mods).Count().IsEqualTo(1);
            await Assert.That(dto.Mods[0].File).IsEqualTo("jei-1.20.1-15.2.0.27.jar");
        }

        [Test]
        public async Task Deserialize_PresentUsername_IsAccepted()
        {
            var dto = JsonSerializer.Deserialize<ClientReportDto>(ReportWithUsername);

            await Assert.That(dto!.Username).IsEqualTo("Alex");
        }

        [Test]
        public async Task Deserialize_MissingUsernameProperty_IsRejected()
        {
            var body = """
                {"clientId":"c","mods":[]}
                """;

            await Assert.That(() => JsonSerializer.Deserialize<ClientReportDto>(body))
                .Throws<JsonException>();
        }

        [Test]
        public async Task Deserialize_MissingClientId_IsRejected()
        {
            var body = """
                {"username":null,"mods":[]}
                """;

            await Assert.That(() => JsonSerializer.Deserialize<ClientReportDto>(body))
                .Throws<JsonException>();
        }

        [Test]
        public async Task Deserialize_FieldNames_SurviveACamelCaseNamingPolicy()
        {
            var dto = JsonSerializer.Deserialize<ClientReportDto>(
                ReportWithUsername,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            await Assert.That(dto!.ClientId).IsEqualTo("3f0f1f4a-4b3f-4d1e-9c0a-2b6d5e8f7a11");
            await Assert.That(dto.Mods[0].Sha256)
                .IsEqualTo("817c44afc9bd5ffa653785e54107b708af0a1b5b695095e0a5235cfe7b24b4f3");
        }

        [Test]
        public async Task Deserialize_FieldNames_SurviveASnakeCaseNamingPolicy()
        {
            var dto = JsonSerializer.Deserialize<ClientReportDto>(
                ReportWithNullUsername,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

            await Assert.That(dto!.ClientId).IsEqualTo("3f0f1f4a-4b3f-4d1e-9c0a-2b6d5e8f7a11");
            await Assert.That(dto.Username).IsNull();
            await Assert.That(dto.Mods[0].File).IsEqualTo("jei-1.20.1-15.2.0.27.jar");
        }
    
        // The side is new and optional. Every jar shipped before it exists sends no side, and those
        // are all clients, so absent has to mean client - the same rule the manifest follows.
        [Test]
        public async Task Deserialize_NoSide_MeansClient()
        {
            var dto = JsonSerializer.Deserialize<ClientReportDto>(ReportWithUsername);

            await Assert.That(dto!.Side).IsNull();
            await Assert.That(ModSideRules.TryParse(dto.Side, out var side)).IsTrue();
            await Assert.That(side).IsEqualTo(SyncSide.Client);
        }

        [Test]
        public async Task Deserialize_ServerSide_IsRead()
        {
            const string body = """
                {"clientId":"c-1","username":null,"side":"server","mods":[]}
                """;

            var dto = JsonSerializer.Deserialize<ClientReportDto>(body);

            await Assert.That(dto!.Side).IsEqualTo("server");
            await Assert.That(ModSideRules.TryParse(dto.Side, out var side)).IsTrue();
            await Assert.That(side).IsEqualTo(SyncSide.Server);
        }

        [Test]
        public async Task Deserialize_AnUnknownSide_StillLeavesTheModListReadable()
        {
            // This arrives from a jar on a machine nobody controls. Losing the whole report over one
            // unrecognised field would be the wrong trade, so the handler falls back to client.
            const string body = """
                {"clientId":"c-1","username":null,"side":"weird","mods":[{"file":"a.jar","sha256":"abc"}]}
                """;

            var dto = JsonSerializer.Deserialize<ClientReportDto>(body);

            await Assert.That(dto!.Mods).Count().IsEqualTo(1);
            await Assert.That(ModSideRules.TryParse(dto.Side, out var side)).IsFalse();
            await Assert.That(side).IsEqualTo(SyncSide.Client);
        }
}
}
