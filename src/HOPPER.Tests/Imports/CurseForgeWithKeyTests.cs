using System.Net;
using HOPPER.Application.Imports;
using HOPPER.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Imports
{
    /// <summary>
    /// The half of <see cref="CurseForgePlanner"/> that only runs once CurseForge:ApiKey is set.
    /// Everything here drives the real <see cref="CurseForgeClient"/> over a canned transport, so
    /// no live key and no network are involved.
    /// </summary>
    public class CurseForgeWithKeyTests
    {
        private const string Sha1 = "c5043f862be7db76892c7c0c95d02fa3f8332af0";
        private const string ManifestOneFile = """
            {"manifestType":"minecraftModpack","manifestVersion":1,"name":"ATM9","version":"1.1.1",
             "minecraft":{"version":"1.20.1","modLoaders":[{"id":"forge-47.4.0","primary":true}]},
             "files":[{"projectID":351491,"fileID":6366217,"required":true}],
             "overrides":"overrides"}
            """;

        private static CurseForgeClient Client(CannedHttp http) =>
            new(http, new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["CurseForge:ApiKey"] = "test-key" })
                .Build());

        private static CannedHttp Responding(string curseForge, string? modrinth = null) =>
            new((url, _) => url.StartsWith("https://api.curseforge.com", StringComparison.Ordinal)
                ? CannedHttp.Ok(curseForge)
                : modrinth is null
                    ? CannedHttp.Json(HttpStatusCode.NotFound, "{}")
                    : CannedHttp.Ok(modrinth));

        private static string Quoted(string? value) => value is null ? "null" : "\"" + value + "\"";

        private static string CurseForgeFile(string? downloadUrl, string? fileName = "jei.jar",
            string? sha1 = Sha1, string? displayName = "JEI 15.3.0.4") =>
            "{\"data\":[{\"id\":6366217,\"modId\":351491"
            + ",\"fileName\":" + Quoted(fileName)
            + ",\"displayName\":" + Quoted(displayName)
            + ",\"downloadUrl\":" + Quoted(downloadUrl)
            + ",\"fileLength\":1234567"
            + ",\"hashes\":" + (sha1 is null ? "[]" : "[{\"value\":\"" + sha1 + "\",\"algo\":1}]")
            + "}]}";

        private const string ModrinthHit =
            "{\"" + Sha1 + "\":{\"files\":[{\"hashes\":{\"sha1\":\"" + Sha1 + "\"}"
            + ",\"url\":\"https://cdn.modrinth.com/data/u6dRKJwZ/versions/x/jei.jar\",\"primary\":true}]}}";

        [Test]
        public async Task CurseForgePlan_WithAKey_TurnsAResolvedFileIntoAPlannedDownload()
        {
            using var archive = PackArchive.Of(
                ("manifest.json", ManifestOneFile),
                ("overrides/mods/cc-tweaked.jar", "PK cc"));

            var http = Responding(CurseForgeFile("https://edge.forgecdn.net/files/6366/217/jei.jar"));
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            await Assert.That(plan.Pending).IsEmpty();

            var file = plan.Files.Single(f => f.FileName == "jei.jar");
            await Assert.That(file.Downloads.Single().Host).IsEqualTo("edge.forgecdn.net");
            await Assert.That(file.Sha1).IsEqualTo(Sha1);
            await Assert.That(file.Size).IsEqualTo(1234567L);
            await Assert.That(file.ZipEntry).IsNull();

            await Assert.That(plan.Files.Select(f => f.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "cc-tweaked.jar", "jei.jar" });
        }

        [Test]
        public async Task CurseForgePlan_AFileTheApiDoesNotReturn_IsPendingDownloadFailedNotNoApiKey()
        {
            using var archive = PackArchive.Of(("manifest.json", ManifestOneFile));

            var http = Responding("""{"data":[]}""");
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            var pending = plan.Pending.Single();
            await Assert.That(pending.Reason).IsEqualTo(PendingReason.DownloadFailed);
            await Assert.That(pending.Detail).IsEqualTo(
                "CurseForge did not return this file. Download the jar and supply it here.");
            await Assert.That(pending.SourceUrl).IsEqualTo("https://www.curseforge.com/projects/351491");
        }

        [Test]
        public async Task CurseForgePlan_ABadApiKey_LeavesEveryEntryPendingDownloadFailed()
        {
            using var archive = PackArchive.Of(("manifest.json", ManifestOneFile));

            var http = new CannedHttp((_, _) => CannedHttp.Json(HttpStatusCode.Forbidden, """{"error":"bad key"}"""));
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            await Assert.That(plan.Pending.Single().Reason).IsEqualTo(PendingReason.DownloadFailed);
            await Assert.That(http.Calls).Count().IsEqualTo(1);
        }

        [Test]
        public async Task CurseForgePlan_AFileWithNoDownloadUrl_IsTakenFromModrinthByItsSha1()
        {
            using var archive = PackArchive.Of(("manifest.json", ManifestOneFile));

            var http = Responding(CurseForgeFile(downloadUrl: null), ModrinthHit);
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            await Assert.That(plan.Pending).IsEmpty();

            var file = plan.Files.Single();
            await Assert.That(file.FileName).IsEqualTo("jei.jar");
            await Assert.That(file.Downloads.Single().Host).IsEqualTo("cdn.modrinth.com");
            await Assert.That(file.Sha1).IsEqualTo(Sha1);

            await Assert.That(http.Calls.Select(c => c.Url).ToList()).IsEquivalentTo(new[]
            {
                "https://api.curseforge.com/v1/mods/files",
                "https://api.modrinth.com/v2/version_files",
            });
        }

        [Test]
        public async Task CurseForgePlan_AFileTheAuthorBlockedAndModrinthDoesNotMirror_IsPendingBlocked()
        {
            using var archive = PackArchive.Of(("manifest.json", ManifestOneFile));

            var http = Responding(CurseForgeFile(downloadUrl: null), modrinth: "{}");
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            await Assert.That(plan.Files).IsEmpty();

            var pending = plan.Pending.Single();
            await Assert.That(pending.Reason).IsEqualTo(PendingReason.Blocked);
            await Assert.That(pending.ExpectedSha1).IsEqualTo(Sha1);
            await Assert.That(pending.FileName).IsEqualTo("jei.jar");
            await Assert.That(pending.DisplayName).IsEqualTo("JEI 15.3.0.4");
            await Assert.That(pending.ProjectId).IsEqualTo(351491);
            await Assert.That(pending.FileId).IsEqualTo(6366217);
            await Assert.That(pending.SourceUrl).IsEqualTo("https://www.curseforge.com/projects/351491");
            await Assert.That(pending.Detail).IsEqualTo(
                "The author disabled third-party distribution for this file. Download it from CurseForge and supply it here.");
        }

        [Test]
        public async Task CurseForgePlan_ABlockedFileWithNoSha1_IsPendingWithoutAskingModrinth()
        {
            using var archive = PackArchive.Of(("manifest.json", ManifestOneFile));

            var http = Responding(CurseForgeFile(downloadUrl: null, sha1: null));
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            await Assert.That(plan.Pending.Single().Reason).IsEqualTo(PendingReason.Blocked);
            await Assert.That(plan.Pending.Single().ExpectedSha1).IsNull();
            await Assert.That(http.Calls.Any(c => c.Url.Contains("modrinth", StringComparison.Ordinal))).IsFalse();
        }

        [Test]
        public async Task CurseForgePlan_AMirroredFileWithNoFileName_IsStillPendingBlocked()
        {
            using var archive = PackArchive.Of(("manifest.json", ManifestOneFile));

            var http = Responding(CurseForgeFile(downloadUrl: null, fileName: null), ModrinthHit);
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            await Assert.That(plan.Files).IsEmpty();
            await Assert.That(plan.Pending.Single().Reason).IsEqualTo(PendingReason.Blocked);
            await Assert.That(plan.Pending.Single().FileName).IsNull();
        }

        [Test]
        public async Task CurseForgePlan_AResolvedFileWithNoFileName_GetsTheGeneratedName()
        {
            using var archive = PackArchive.Of(("manifest.json", ManifestOneFile));

            var http = Responding(CurseForgeFile("https://edge.forgecdn.net/files/6366/217/jei.jar", fileName: null));
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            await Assert.That(plan.Files.Single().FileName).IsEqualTo("curseforge-351491-6366217.jar");
        }

        [Test]
        public async Task CurseForgePlan_ModListLabels_StillWinWhenTheApiHasNoDisplayName()
        {
            using var archive = PackArchive.Of(
                ("manifest.json", ManifestOneFile),
                ("modlist.html", "<ul><li><a href=\"x\">Just Enough Items (by mezz)</a></li></ul>"));

            var http = Responding(CurseForgeFile(downloadUrl: null, displayName: null), modrinth: "{}");
            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, Client(http), CancellationToken.None);

            await Assert.That(plan.Pending.Single().DisplayName).IsEqualTo("Just Enough Items (by mezz)");
        }
    }
}
