using HOPPER.Application;
using HOPPER.Application.Command.Mods;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Tests.Application
{
    public class SetModSideCommandHandlerTests
    {
        private static readonly Guid ServerA = Guid.NewGuid();

        private static readonly Guid ServerB = Guid.NewGuid();

        private static HopperDbContext NewDb() =>
            new(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-{Guid.NewGuid():N}")
                .Options);

        private static Mod NewMod(Guid serverId, string fileName) => new()
        {
            ServerId = serverId,
            FileName = fileName,
            Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Size = 1,
        };

        [Test]
        public async Task Handle_NewMod_DefaultsToBoth()
        {
            await using var db = NewDb();
            var mod = NewMod(ServerA, "jei.jar");
            db.Mods.Add(mod);
            await db.SaveChangesAsync();

            await Assert.That(db.Mods.Single().Side).IsEqualTo(ModSide.Both);
        }

        [Test]
        public async Task Handle_SetsTheSideOnEveryNamedMod()
        {
            await using var db = NewDb();
            var a = NewMod(ServerA, "jei.jar");
            var b = NewMod(ServerA, "jade.jar");
            db.Mods.AddRange(a, b);
            await db.SaveChangesAsync();

            var updated = await new SetModSideCommandHandler(db)
                .Handle(new SetModSideCommand(ServerA, [a.Id, b.Id], ModSide.ClientOnly), CancellationToken.None);

            await Assert.That(updated).IsEqualTo(2);
            await Assert.That(db.Mods.Select(m => m.Side).Distinct().Single()).IsEqualTo(ModSide.ClientOnly);
        }

        [Test]
        public async Task Handle_ModOnAnotherServer_IsNotTouched()
        {
            await using var db = NewDb();
            var mine = NewMod(ServerA, "jei.jar");
            var theirs = NewMod(ServerB, "jade.jar");
            db.Mods.AddRange(mine, theirs);
            await db.SaveChangesAsync();

            var updated = await new SetModSideCommandHandler(db)
                .Handle(new SetModSideCommand(ServerA, [mine.Id, theirs.Id], ModSide.ServerOnly), CancellationToken.None);

            await Assert.That(updated).IsEqualTo(1);
            await Assert.That(db.Mods.Single(m => m.Id == mine.Id).Side).IsEqualTo(ModSide.ServerOnly);
            await Assert.That(db.Mods.Single(m => m.Id == theirs.Id).Side).IsEqualTo(ModSide.Both);
        }

        [Test]
        public async Task Handle_RepeatedIds_CountsEachModOnce()
        {
            await using var db = NewDb();
            var mod = NewMod(ServerA, "jei.jar");
            db.Mods.Add(mod);
            await db.SaveChangesAsync();

            var updated = await new SetModSideCommandHandler(db)
                .Handle(new SetModSideCommand(ServerA, [mod.Id, mod.Id], ModSide.ClientOnly), CancellationToken.None);

            await Assert.That(updated).IsEqualTo(1);
        }

        [Test]
        public async Task Handle_NoIds_ChangesNothing()
        {
            await using var db = NewDb();
            db.Mods.Add(NewMod(ServerA, "jei.jar"));
            await db.SaveChangesAsync();

            var updated = await new SetModSideCommandHandler(db)
                .Handle(new SetModSideCommand(ServerA, [], ModSide.ServerOnly), CancellationToken.None);

            await Assert.That(updated).IsEqualTo(0);
            await Assert.That(db.Mods.Single().Side).IsEqualTo(ModSide.Both);
        }

        [Test]
        public async Task Handle_UnknownId_MatchesNothing()
        {
            await using var db = NewDb();
            db.Mods.Add(NewMod(ServerA, "jei.jar"));
            await db.SaveChangesAsync();

            var updated = await new SetModSideCommandHandler(db)
                .Handle(new SetModSideCommand(ServerA, [Guid.NewGuid()], ModSide.ClientOnly), CancellationToken.None);

            await Assert.That(updated).IsEqualTo(0);
        }

        [Test]
        public async Task Handle_SideOutsideTheEnum_IsRejected()
        {
            await using var db = NewDb();
            var mod = NewMod(ServerA, "jei.jar");
            db.Mods.Add(mod);
            await db.SaveChangesAsync();

            await Assert.That(async () => await new SetModSideCommandHandler(db)
                    .Handle(new SetModSideCommand(ServerA, [mod.Id], (ModSide)99), CancellationToken.None))
                .Throws<InvalidRequestException>();

            await Assert.That(db.Mods.Single().Side).IsEqualTo(ModSide.Both);
        }
    }
}
