using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HOPPER.Tests.Infrastructure
{
    public static class PostgresHarness
    {
        private static readonly Lazy<PostgreSqlContainer> Container = new(() =>
        {
            var postgres = new PostgreSqlBuilder("postgres:18.3").WithCleanUp(true).Build();
            postgres.StartAsync().GetAwaiter().GetResult();

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { postgres.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch { }
            };

            return postgres;
        });

        public static async Task<string> NewDatabaseAsync()
        {
            var name = "hopper_" + Guid.NewGuid().ToString("N");
            var admin = Container.Value.GetConnectionString();

            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{name}\"";
                await command.ExecuteNonQueryAsync();
            }

            return new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
        }

        public static HopperDbContext Context(string connectionString) =>
            new(new DbContextOptionsBuilder<HopperDbContext>().UseNpgsql(connectionString).Options);

        public static async Task<string> NewMigratedDatabaseAsync()
        {
            var connectionString = await NewDatabaseAsync();

            await using var db = Context(connectionString);
            await db.Database.MigrateAsync();

            return connectionString;
        }
    }
}
