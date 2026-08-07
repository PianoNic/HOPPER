using HOPPER.Application.Command.Servers;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Tests.Application
{
    public class ServerSlugTests
    {
        private static HopperDbContext NewDb() =>
            new(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-{Guid.NewGuid():N}")
                .Options);

        [Test]
        public async Task Create_DerivesTheSlugFromTheName()
        {
            await using var db = NewDb();
            var handler = new CreateServerCommandHandler(db);

            var server = await handler.Handle(new CreateServerCommand("Survival SMP", null, ModLoader.Forge), CancellationToken.None);

            await Assert.That(server.Slug).IsEqualTo("survival-smp");
        }

        // Two people naming their server "Survival" is ordinary, and the second must not be met
        // with a conflict they cannot act on: they never chose the slug in the first place.
        [Test]
        public async Task Create_GivesASecondServerOfTheSameNameItsOwnSlug()
        {
            await using var db = NewDb();
            var handler = new CreateServerCommandHandler(db);

            var first = await handler.Handle(new CreateServerCommand("Survival", null, ModLoader.Forge), CancellationToken.None);
            var second = await handler.Handle(new CreateServerCommand("Survival", null, ModLoader.Forge), CancellationToken.None);

            await Assert.That(first.Slug).IsEqualTo("survival");
            await Assert.That(second.Slug).IsNotEqualTo(first.Slug);
            await Assert.That(second.Slug).StartsWith("survival");
        }

        // The slug names the generated jar and is the readable half of a URL an admin may already
        // have handed out. A rename must not move it.
        [Test]
        public async Task Update_LeavesTheSlugWhereItWas()
        {
            await using var db = NewDb();
            var created = await new CreateServerCommandHandler(db)
                .Handle(new CreateServerCommand("Survival SMP", null, ModLoader.Forge), CancellationToken.None);

            var renamed = await new UpdateServerCommandHandler(db)
                .Handle(new UpdateServerCommand(created.Id, "Something Else Entirely"), CancellationToken.None);

            await Assert.That(renamed.Name).IsEqualTo("Something Else Entirely");
            await Assert.That(renamed.Slug).IsEqualTo("survival-smp");
        }

        // Without one the browse page cannot search, the jar endpoint 400s and the loader version
        // list has nothing to go on: a server that cannot do the thing HOPPER is for.
        [Test]
        public async Task Create_RefusesAServerWithNoLoader()
        {
            await using var db = NewDb();
            var handler = new CreateServerCommandHandler(db);

            await Assert.That(async () => await handler.Handle(
                    new CreateServerCommand("Survival", null, ModLoader.Unknown), CancellationToken.None))
                .Throws<ArgumentException>();
        }

        // Update deliberately still accepts it, or a server created before the rule could not be
        // renamed until someone guessed at its loader.
        [Test]
        public async Task Update_StillAcceptsAServerThatHasNoLoader()
        {
            await using var db = NewDb();
            var created = await new CreateServerCommandHandler(db)
                .Handle(new CreateServerCommand("Survival SMP", null, ModLoader.Forge), CancellationToken.None);

            var renamed = await new UpdateServerCommandHandler(db)
                .Handle(new UpdateServerCommand(created.Id, "Renamed", null, ModLoader.Unknown), CancellationToken.None);

            await Assert.That(renamed.Name).IsEqualTo("Renamed");
        }

        [Test]
        public async Task Create_RefusesANameNoSlugCanComeFrom()
        {
            await using var db = NewDb();
            var handler = new CreateServerCommandHandler(db);

            await Assert.That(async () => await handler.Handle(new CreateServerCommand("???", null, ModLoader.Forge), CancellationToken.None))
                .Throws<ArgumentException>();
        }
    }
}
