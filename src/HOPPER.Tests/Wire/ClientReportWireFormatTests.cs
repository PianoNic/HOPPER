using System.Text.Json;
using HOPPER.Application.Dtos.Clients;

namespace HOPPER.Tests.Wire
{
    /// <summary>
    /// The inbound half of the fixed wire format. The bodies below are exactly what
    /// Syncer.report(String) emits — Gson with serializeNulls(), component names as JSON field
    /// names — so if these parse, the shipped client's report parses.
    /// </summary>
    public class ClientReportWireFormatTests
    {
        // Verbatim from Syncer: GSON.toJson(new Report(clientId(), username, installed)) with a null
        // username, which is what HopperLocator.username() returns on a dedicated server or under any
        // launcher started without --username.
        private const string ReportWithNullUsername = """
            {"clientId":"3f0f1f4a-4b3f-4d1e-9c0a-2b6d5e8f7a11","username":null,"mods":[{"file":"jei-1.20.1-15.2.0.27.jar","sha256":"817c44afc9bd5ffa653785e54107b708af0a1b5b695095e0a5235cfe7b24b4f3"}]}
            """;

        private const string ReportWithUsername = """
            {"clientId":"3f0f1f4a-4b3f-4d1e-9c0a-2b6d5e8f7a11","username":"Alex","mods":[{"file":"jei-1.20.1-15.2.0.27.jar","sha256":"817c44afc9bd5ffa653785e54107b708af0a1b5b695095e0a5235cfe7b24b4f3"}]}
            """;

        [Test]
        public async Task Deserialize_NullUsername_IsAccepted()
        {
            // The regression this test exists for: with a non-nullable Username the request never
            // reached a handler at all — model binding answered 400 — and because Syncer.report()
            // swallows every failure, the client simply never appeared on the dashboard.
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
            // Nullable, but still required: the shipped client always sends the property (that is what
            // serializeNulls() is for), so a body without it is not a HOPPER client and should not be
            // quietly recorded as one.
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
            // Under a snake_case policy an unpinned ClientId would be read from "client_id" and the
            // shipped "clientId" would be dropped on the floor, so every report would arrive with a
            // null client id.
            var dto = JsonSerializer.Deserialize<ClientReportDto>(
                ReportWithNullUsername,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

            await Assert.That(dto!.ClientId).IsEqualTo("3f0f1f4a-4b3f-4d1e-9c0a-2b6d5e8f7a11");
            await Assert.That(dto.Username).IsNull();
            await Assert.That(dto.Mods[0].File).IsEqualTo("jei-1.20.1-15.2.0.27.jar");
        }
    }
}
