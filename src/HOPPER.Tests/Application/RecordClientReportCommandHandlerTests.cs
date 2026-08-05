using HOPPER.Application.Command.Clients;
using HOPPER.Application.Dtos.Clients;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Tests.Application
{
    public class RecordClientReportCommandHandlerTests
    {
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

        [Test]
        public async Task Handle_NullUsername_RecordsTheClient()
        {
            // A dedicated server has no username to report. Before this was allowed, every such
            // install was invisible on the dashboard - and invisibly so, because Syncer.report()
            // logs a warning and moves on.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("server-1", null, ("jei.jar", "aa")), "10.0.0.1"), CancellationToken.None);

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
            // One "no username" state for the dashboard to render instead of three.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("c1", username), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).IsNull();
        }

        [Test]
        public async Task Handle_UsernameWithSurroundingWhitespace_IsTrimmed()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("c1", "  Alex  "), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).IsEqualTo("Alex");
        }

        [Test]
        public async Task Handle_PresentUsername_IsRecorded()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("c1", "Alex"), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).IsEqualTo("Alex");
        }

        [Test]
        public async Task Handle_SecondReport_UpsertsRatherThanCreatingASecondRow()
        {
            // There is no registration step - a client exists exactly when it has reported once - so
            // the clientId has to behave as the natural key across launches.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("c1", null, ("jei.jar", "aa")), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(Report("c1", "Alex", ("jei.jar", "aa")), null), CancellationToken.None);

            await Assert.That(await db.Clients.CountAsync()).IsEqualTo(1);
            await Assert.That((await db.Clients.SingleAsync()).Username).IsEqualTo("Alex");
        }

        [Test]
        public async Task Handle_ClientThatLosesItsUsername_GoesBackToNull()
        {
            // The same install can report a username on Monday (Prism) and none on Tuesday (a server
            // start). The later report is the truth; a sticky old username would misattribute a row.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("c1", "Alex"), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(Report("c1", null), null), CancellationToken.None);

            await Assert.That((await db.Clients.SingleAsync()).Username).IsNull();
        }

        [Test]
        public async Task Handle_SecondReport_ReplacesTheReportedSetWholesale()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("c1", null, ("jei.jar", "aa"), ("rei.jar", "bb")), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(Report("c1", null, ("jei.jar", "cc")), null), CancellationToken.None);

            var rows = await db.ClientReportedMods.ToListAsync();
            await Assert.That(rows).Count().IsEqualTo(1);
            await Assert.That(rows[0].FileName).IsEqualTo("jei.jar");
            await Assert.That(rows[0].Sha256).IsEqualTo("cc");
        }

        [Test]
        public async Task Handle_ReportFromAnotherClient_IsNotDisturbed()
        {
            // The wholesale replace is scoped to one client; a shared friend group would otherwise
            // wipe each other's inventories on every launch.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("c1", "Alex", ("jei.jar", "aa")), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(Report("c2", null, ("rei.jar", "bb")), null), CancellationToken.None);
            await handler.Handle(new RecordClientReportCommand(Report("c2", null, ("rei.jar", "cc")), null), CancellationToken.None);

            var c1 = await db.Clients.SingleAsync(c => c.ClientId == "c1");
            var c1Mods = await db.ClientReportedMods.Where(r => r.ClientId == c1.Id).ToListAsync();
            await Assert.That(c1Mods).Count().IsEqualTo(1);
            await Assert.That(c1Mods[0].Sha256).IsEqualTo("aa");
        }

        [Test]
        [Arguments("")]
        [Arguments("   ")]
        public async Task Handle_BlankClientId_IsRejected(string clientId)
        {
            // Unlike the username this one is not optional: it is the row's identity, and the shipped
            // client always has one (hopper/client-id, generated on first run).
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await Assert.That(async () => await handler.Handle(
                    new RecordClientReportCommand(Report(clientId, "Alex"), null), CancellationToken.None))
                .Throws<ArgumentException>();
        }

        [Test]
        public async Task Handle_ReportedModWithoutAHash_IsRejected()
        {
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await Assert.That(async () => await handler.Handle(
                    new RecordClientReportCommand(Report("c1", "Alex", ("jei.jar", "")), null), CancellationToken.None))
                .Throws<ArgumentException>();
        }

        [Test]
        public async Task Handle_EmptyModSet_IsAccepted()
        {
            // A fresh client with an empty manifest reports zero jars. That is a valid state, not an
            // error, and it still has to put the client on the dashboard.
            await using var db = NewDb();
            var handler = new RecordClientReportCommandHandler(db);

            await handler.Handle(new RecordClientReportCommand(Report("c1", null), null), CancellationToken.None);

            await Assert.That(await db.Clients.CountAsync()).IsEqualTo(1);
            await Assert.That(await db.ClientReportedMods.CountAsync()).IsEqualTo(0);
        }
    }
}
