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
            // Only the flag. Testcontainers appends to the command the Postgres module already
            // configured, so passing the "postgres" argv[0] as well splices two commands together
            // and the container exits 1 before anything listens.
            var postgres = new PostgreSqlBuilder("postgres:18.3")
                .WithCommand("-c", "max_connections=500")
                .WithCleanUp(true)
                .Build();
            postgres.StartAsync().GetAwaiter().GetResult();

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { postgres.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch { }
            };

            return postgres;
        });

        // One CREATE DATABASE per test through a pool Npgsql would otherwise let grow to a hundred
        // idle connections, on a server every test is also holding one of its own.
        private static string Admin() =>
            new NpgsqlConnectionStringBuilder(Container.Value.GetConnectionString()) { MaxPoolSize = 8 }.ConnectionString;

        public static async Task<string> NewDatabaseAsync()
        {
            var name = "hopper_" + Guid.NewGuid().ToString("N");
            var admin = Admin();

            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{name}\"";
                await command.ExecuteNonQueryAsync();
            }

            // Unpooled on purpose. Every test gets its own database and therefore its own pool, and a
            // pool holds its connections idle for the rest of the run, so pooling here accumulates
            // connections the suite never uses again until the server refuses new ones.
            return new NpgsqlConnectionStringBuilder(admin)
            {
                Database = name,
                Pooling = false,
            }.ConnectionString;
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
