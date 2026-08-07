using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HOPPER.Tests.Infrastructure
{
    public class ModsMigrationTests
    {
        private const string BeforeThisChange = "20260806140706_AddModModIds";

        private static async Task<string> AtTheOldSchemaAsync()
        {
            var connectionString = await PostgresHarness.NewDatabaseAsync();

            await using var db = PostgresHarness.Context(connectionString);
            await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeThisChange);

            return connectionString;
        }

        private static async Task MigrateToLatestAsync(string connectionString)
        {
            await using var db = PostgresHarness.Context(connectionString);
            await db.Database.MigrateAsync();
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private static async Task SeedServerAsync(string connectionString, Guid id, string slug) =>
            await ExecuteAsync(connectionString,
                $"""
                 INSERT INTO "Servers" ("Id", "Name", "Slug", "Token", "Loader", "CreatedAt", "UpdatedAt")
                 VALUES ('{id}', 'Server {slug}', '{slug}', '{Guid.NewGuid():N}', 0, now(), now());
                 """);

        private static async Task SeedModAsync(
            string connectionString, Guid serverId, string fileName, string sha, DateTime createdAt) =>
            await ExecuteAsync(connectionString,
                $"""
                 INSERT INTO "Mods" ("Id", "ServerId", "FileName", "Sha256", "Size", "Source", "CreatedAt", "UpdatedAt")
                 VALUES ('{Guid.NewGuid()}', '{serverId}', '{fileName}', '{sha}', 10, 0,
                         TIMESTAMPTZ '{createdAt:yyyy-MM-dd HH:mm:ss.ffffff}+00',
                         TIMESTAMPTZ '{createdAt:yyyy-MM-dd HH:mm:ss.ffffff}+00');
                 """);

        [Test]
        public async Task Migration_OrdinaryRows_SurviveUnchanged()
        {
            var connectionString = await AtTheOldSchemaAsync();
            var serverId = Guid.NewGuid();
            await SeedServerAsync(connectionString, serverId, "survivors");

            var jei = new string('1', 64);
            var rei = new string('2', 64);
            await SeedModAsync(connectionString, serverId, "jei.jar", jei, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await SeedModAsync(connectionString, serverId, "rei.jar", rei, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            await MigrateToLatestAsync(connectionString);

            await using var db = PostgresHarness.Context(connectionString);
            var rows = await db.Mods.AsNoTracking().OrderBy(m => m.FileName).ToListAsync();

            await Assert.That(rows.Select(r => r.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar", "rei.jar" });
            await Assert.That(rows.Select(r => r.Sha256).ToList()).IsEquivalentTo(new[] { jei, rei });
            await Assert.That(rows.All(r => r.Size == 10)).IsTrue();
            await Assert.That((await db.Servers.AsNoTracking().SingleAsync()).Slug).IsEqualTo("survivors");
        }

        [Test]
        public async Task Migration_CaseVariantFileNamesOnOneServer_KeepsTheOldestRow()
        {
            var connectionString = await AtTheOldSchemaAsync();
            var serverId = Guid.NewGuid();
            await SeedServerAsync(connectionString, serverId, "case-variants");

            var oldest = new string('a', 64);
            var newer = new string('b', 64);
            await SeedModAsync(connectionString, serverId, "JEI.jar", oldest, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await SeedModAsync(connectionString, serverId, "jei.jar", newer, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

            await MigrateToLatestAsync(connectionString);

            await using var db = PostgresHarness.Context(connectionString);
            var rows = await db.Mods.AsNoTracking().ToListAsync();

            await Assert.That(rows).Count().IsEqualTo(1);
            await Assert.That(rows[0].FileName).IsEqualTo("JEI.jar");
            await Assert.That(rows[0].Sha256).IsEqualTo(oldest);
        }

        [Test]
        public async Task Migration_CaseVariantFileNamesOnDifferentServers_KeepsBoth()
        {
            var connectionString = await AtTheOldSchemaAsync();
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            await SeedServerAsync(connectionString, a, "server-a");
            await SeedServerAsync(connectionString, b, "server-b");

            await SeedModAsync(connectionString, a, "JEI.jar", new string('a', 64), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await SeedModAsync(connectionString, b, "jei.jar", new string('b', 64), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            await MigrateToLatestAsync(connectionString);

            await using var db = PostgresHarness.Context(connectionString);

            await Assert.That(await db.Mods.CountAsync()).IsEqualTo(2);
        }

        [Test]
        public async Task Migration_FileNameLongerThan255_IsTruncatedAndTheIndexStillBuilds()
        {
            var connectionString = await AtTheOldSchemaAsync();
            var serverId = Guid.NewGuid();
            await SeedServerAsync(connectionString, serverId, "long-names");

            var overlong = new string('x', 300) + ".jar";
            await SeedModAsync(connectionString, serverId, overlong, new string('c', 64), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            await MigrateToLatestAsync(connectionString);

            await using var db = PostgresHarness.Context(connectionString);
            var row = await db.Mods.AsNoTracking().SingleAsync();

            await Assert.That(row.FileName).Length().IsEqualTo(255);
            await Assert.That(row.FileName).IsEqualTo(overlong[..255]);
        }

        [Test]
        public async Task Migration_AfterUpgrading_TheLowerIndexRefusesACaseVariantInsert()
        {
            var connectionString = await AtTheOldSchemaAsync();
            var serverId = Guid.NewGuid();
            await SeedServerAsync(connectionString, serverId, "refuses");
            await SeedModAsync(connectionString, serverId, "jei.jar", new string('d', 64), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            await MigrateToLatestAsync(connectionString);

            await Assert.That(async () => await SeedModAsync(
                    connectionString, serverId, "JEI.jar", new string('e', 64), new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)))
                .Throws<PostgresException>();
        }

        [Test]
        public async Task Migration_ClientRowsWithAJunkHash_AreDroppedAndTheRestSurvive()
        {
            var connectionString = await AtTheOldSchemaAsync();
            var serverId = Guid.NewGuid();
            await SeedServerAsync(connectionString, serverId, "client-rows");

            var clientId = Guid.NewGuid();
            await ExecuteAsync(connectionString,
                $"""
                 INSERT INTO "Clients" ("Id", "ServerId", "ClientId", "Username", "LastSeenAt", "CreatedAt", "UpdatedAt")
                 VALUES ('{clientId}', '{serverId}', '{new string('c', 400)}', 'alex', now(), now(), now());

                 INSERT INTO "ClientReportedMods" ("Id", "ClientId", "FileName", "Sha256", "CreatedAt", "UpdatedAt")
                 VALUES ('{Guid.NewGuid()}', '{clientId}', 'jei.jar', '{new string('f', 64)}', now(), now()),
                        ('{Guid.NewGuid()}', '{clientId}', 'rei.jar', 'aa', now(), now());
                 """);

            await MigrateToLatestAsync(connectionString);

            await using var db = PostgresHarness.Context(connectionString);

            var client = await db.Clients.AsNoTracking().SingleAsync();
            await Assert.That(client.ClientId).Length().IsEqualTo(200);

            var reported = await db.ClientReportedMods.AsNoTracking().ToListAsync();
            await Assert.That(reported).Count().IsEqualTo(1);
            await Assert.That(reported[0].FileName).IsEqualTo("jei.jar");
        }
    }
}
