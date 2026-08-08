using HOPPER.Application;
using HOPPER.Application.Command.Clients;
using HOPPER.Application.Dtos.Clients;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Tests.Application
{
    public class RecordClientReportCommandHandlerTests
    {
        private static readonly Guid ServerId = Guid.NewGuid();

        private const string ShaA = "817c44afc9bd5ffa653785e54107b708af0a1b5b695095e0a5235cfe7b24b4f3";

        private const string ShaB = "2ab6f0e33a0e0e3d7f4f2b6a1c9d8e7f605142333a2b1c0d9e8f7a6b5c4d3e2f";

        private const string ShaC = "f1e2d3c4b5a69788796a5b4c3d2e1f00112233445566778899aabbccddeeff01";

        private static HopperDbContext NewDb() =>
            new(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-{Guid.NewGuid():N}")
                .Options);

        private static ClientReportDto Report(string clientId, string? username, params (string File, string Sha)[] mods) => new()
        {
            ClientId = clientId,
            Username = username,
            Mods = mods.Select(m => new ClientReportModDto { File = m.File, Sha256 = m.Sha }).ToList(),
        };

        private static ClientReportDto Report(string clientId, string? username, string? side,
            params (string File, string Sha)[] mods) => new()
        {
            ClientId = clientId,
            Username = username,
            Side = side,
            Mods = mods.Select(m => new ClientReportModDto { File = m.File, Sha256 = m.Sha }).ToList(),
        };

        [Test]
        public async Task Handle_TheSamePlayerWithANewClientId_TakesOverTheirRow()
        {
            // The client id lives in hoppermods/client-id; wiping or copying an instance mints a new
            // one, and the player was showing up twice.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("old-id", "PianoNic", ("jei.jar", ShaA)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("new-id", "PianoNic", ("jade.jar", ShaB)), null), CancellationToken.None);

            var client = await db.Clients.SingleAsync();

            await Assert.That(client.ClientId).IsEqualTo("new-id");
            await Assert.That(client.Username).IsEqualTo("PianoNic");

            // The report replaces what the row had, so nothing is left over from the old id.
            var reported = await db.ClientReportedMods.ToListAsync();
            await Assert.That(reported.Count).IsEqualTo(1);
            await Assert.That(reported.Single().Sha256).IsEqualTo(ShaB);
        }

        [Test]
        public async Task Handle_TwoAnonymousClients_StayTwoRows()
        {
            // An offline launch reports no username. Two of those are not evidence of one player.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("a", null, ("jei.jar", ShaA)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("b", null, ("jei.jar", ShaA)), null), CancellationToken.None);

            await Assert.That(await db.Clients.CountAsync()).IsEqualTo(2);
        }

        [Test]
        public async Task Handle_AServerAndAPlayerSharingAName_StayTwoRows()
        {
            // Otherwise a dedicated server and its owner collapse into one row.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", "PianoNic", "client", ("jei.jar", ShaA)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("s1", "PianoNic", "server", ("jei.jar", ShaA)), null), CancellationToken.None);

            await Assert.That(await db.Clients.CountAsync()).IsEqualTo(2);
        }

        [Test]
        public async Task Handle_ADifferentPlayer_IsStillTheirOwnRow()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("a", "PianoNic", ("jei.jar", ShaA)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("b", "Someone", ("jei.jar", ShaA)), null), CancellationToken.None);

            await Assert.That(await db.Clients.CountAsync()).IsEqualTo(2);
        }

        [Test]
        public async Task Handle_TheSameNameOnAnotherServer_IsNotTheSameClient()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("a", "PianoNic", ("jei.jar", ShaA)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(Guid.NewGuid(), Report("b", "PianoNic", ("jei.jar", ShaA)), null), CancellationToken.None);

            await Assert.That(await db.Clients.CountAsync()).IsEqualTo(2);
        }

        [Test]
        public async Task Handle_NullUsername_RecordsTheClient()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("server-1", null, ("jei.jar", ShaA)), "10.0.0.1"), CancellationToken.None);

            var client = await db.Clients.SingleAsync();
            await Assert.That(client.ClientId).IsEqualTo("server-1");
            await Assert.That(client.Username).IsNull();
            await Assert.That(client.LastIpAddress).IsEqualTo("10.0.0.1");
            await Assert.That(await db.ClientReportedMods.CountAsync()).IsEqualTo(1);
        }

        [Test]
        [Arguments("")]
        [Arguments("   ")]
        public async Task Handle_BlankUsername_IsStoredAsNull(string username)
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", username), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).IsNull();
        }

        [Test]
        public async Task Handle_UsernameWithSurroundingWhitespace_IsTrimmed()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", "  Alex  "), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).IsEqualTo("Alex");
        }

        [Test]
        public async Task Handle_PresentUsername_IsRecorded()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", "Alex"), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).IsEqualTo("Alex");
        }

        [Test]
        public async Task Handle_SecondReport_UpsertsRatherThanCreatingASecondRow()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", null, ("jei.jar", ShaA)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", "Alex", ("jei.jar", ShaA)), null), CancellationToken.None);

            await Assert.That(await db.Clients.CountAsync()).IsEqualTo(1);
            await Assert.That((await db.Clients.SingleAsync()).Username).IsEqualTo("Alex");
        }

        [Test]
        public async Task Handle_ClientThatLosesItsUsername_GoesBackToNull()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", "Alex"), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", null), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).IsNull();
        }

        [Test]
        public async Task Handle_SecondReport_ReplacesTheReportedSetWholesale()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", null, ("jei.jar", ShaA), ("rei.jar", ShaB)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", null, ("jei.jar", ShaC)), null), CancellationToken.None);

            var rows = await db.ClientReportedMods.ToListAsync();
            await Assert.That(rows).Count().IsEqualTo(1);
            await Assert.That(rows[0].FileName).IsEqualTo("jei.jar");
            await Assert.That(rows[0].Sha256).IsEqualTo(ShaC);
        }

        [Test]
        public async Task Handle_ReportFromAnotherClient_IsNotDisturbed()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", "Alex", ("jei.jar", ShaA)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c2", null, ("rei.jar", ShaB)), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c2", null, ("rei.jar", ShaC)), null), CancellationToken.None);

            var c1 = await db.Clients.SingleAsync(c => c.ClientId == "c1");
            var c1Mods = await db.ClientReportedMods.Where(r => r.ClientId == c1.Id).ToListAsync();
            await Assert.That(c1Mods).Count().IsEqualTo(1);
            await Assert.That(c1Mods[0].Sha256).IsEqualTo(ShaA);
        }

        [Test]
        [Arguments("")]
        [Arguments("   ")]
        public async Task Handle_BlankClientId_IsRejected(string clientId)
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await Assert.That(async () => await handler.Handle(
                    new RecordClientReportCommand(ServerId, Report(clientId, "Alex"), null), CancellationToken.None))
                .Throws<InvalidClientReportException>();
        }

        [Test]
        public async Task Handle_ReportedModWithoutAHash_IsRejected()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await Assert.That(async () => await handler.Handle(
                    new RecordClientReportCommand(ServerId, Report("c1", "Alex", ("jei.jar", "")), null), CancellationToken.None))
                .Throws<InvalidClientReportException>();
        }

        [Test]
        public async Task Handle_MoreModsThanTheCap_IsRejected()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.ConfigWith(("Hopper:MaxReportedMods", "3")));

            var mods = Enumerable.Range(0, 4).Select(i => ($"mod{i}.jar", ShaA)).ToArray();

            await Assert.That(async () => await handler.Handle(
                    new RecordClientReportCommand(ServerId, Report("c1", null, mods), null), CancellationToken.None))
                .Throws<InvalidClientReportException>();
        }

        [Test]
        public async Task Handle_ExactlyTheCap_IsAccepted()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.ConfigWith(("Hopper:MaxReportedMods", "3")));

            var mods = Enumerable.Range(0, 3).Select(i => ($"mod{i}.jar", ShaA)).ToArray();

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", null, mods), null), CancellationToken.None);

            await Assert.That(await db.ClientReportedMods.CountAsync()).IsEqualTo(3);
        }

        [Test]
        public async Task Handle_ADefaultSizedRealPack_IsAccepted()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            var mods = Enumerable.Range(0, 400).Select(i => ($"mod{i}.jar", ShaA)).ToArray();

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", null, mods), null), CancellationToken.None);

            await Assert.That(await db.ClientReportedMods.CountAsync()).IsEqualTo(400);
        }

        [Test]
        [Arguments("aa")]
        [Arguments("not-a-hash-at-all")]
        [Arguments("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
        public async Task Handle_Sha256ThatIsNot64Hex_IsRejected(string sha)
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await Assert.That(async () => await handler.Handle(
                    new RecordClientReportCommand(ServerId, Report("c1", null, ("jei.jar", sha)), null), CancellationToken.None))
                .Throws<InvalidClientReportException>();
        }

        [Test]
        public async Task Handle_UppercaseSha256_IsStoredLowercase()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(
                new RecordClientReportCommand(ServerId, Report("c1", null, ("jei.jar", ShaA.ToUpperInvariant())), null),
                CancellationToken.None);

            await Assert.That((await db.ClientReportedMods.SingleAsync()).Sha256).IsEqualTo(ShaA);
        }

        [Test]
        [Arguments("../../etc/passwd.jar")]
        [Arguments("mods/jei.jar")]
        [Arguments("readme.txt")]
        public async Task Handle_FileNameThatTheValidatorRefuses_IsRejected(string fileName)
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await Assert.That(async () => await handler.Handle(
                    new RecordClientReportCommand(ServerId, Report("c1", null, (fileName, ShaA)), null), CancellationToken.None))
                .Throws<InvalidModFileNameException>();
        }

        [Test]
        public async Task Handle_OversizedClientId_IsRejected()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await Assert.That(async () => await handler.Handle(
                    new RecordClientReportCommand(ServerId, Report(new string('c', 400), null), null), CancellationToken.None))
                .Throws<InvalidClientReportException>();
        }

        [Test]
        public async Task Handle_OversizedUsername_IsTruncatedRatherThanRejected()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(
                new RecordClientReportCommand(ServerId, Report("c1", new string('u', 400)), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).Length().IsEqualTo(100);
        }

        [Test]
        public async Task Handle_EmptyModSet_IsAccepted()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db, TestLimits.Config);

            await handler.Handle(new RecordClientReportCommand(ServerId, Report("c1", null), null), CancellationToken.None);

            await Assert.That(await db.Clients.CountAsync()).IsEqualTo(1);
            await Assert.That(await db.ClientReportedMods.CountAsync()).IsEqualTo(0);
        }
    }
}
