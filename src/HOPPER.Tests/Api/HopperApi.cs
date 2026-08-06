using HOPPER.Domain;
using HOPPER.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HOPPER.Tests.Api
{
    /// <summary>
    /// Boots the real API in-process. One host for the whole test session: the environment variables
    /// below have to be set before the entry point reads them (Program.cs reads configuration eagerly
    /// while registering services), so they are applied inside the Lazy initializer and the host is
    /// created exactly once, under Lazy's own lock.
    ///
    /// The environment is "Testing" rather than "Development" or "Production" on purpose:
    /// Development would load appsettings.Development.json and point the JWT handler at the local
    /// dev IdP - a network dependency this suite must not have - while Production would mount the SPA
    /// middleware, which needs a wwwroot the Dockerfile builds and a test run does not have.
    ///
    /// HOPPER is Postgres-only, so the suite starts a throwaway Postgres container rather than
    /// testing against a different engine than production runs. This needs Docker; without it the
    /// suite fails at startup, which is the honest outcome - a green run against SQLite would prove
    /// nothing about the database HOPPER actually ships on.
    /// </summary>
    public static class HopperApi
    {
        /// <summary>Token of server A, the one almost every test works against.</summary>
        public const string ClientToken = "test-client-token";

        /// <summary>Token of server B, which exists so isolation can be asserted rather than assumed:
        /// a suite with one server cannot tell "scoped correctly" apart from "not scoped at all".</summary>
        public const string ClientTokenB = "test-client-token-b";

        private static Guid _serverAId;
        private static Guid _serverBId;

        public static Guid ServerAId
        {
            get { _ = Instance.Value; return _serverAId; }
        }

        public static Guid ServerBId
        {
            get { _ = Instance.Value; return _serverBId; }
        }

        private static readonly Lazy<WebApplicationFactory<Program>> Instance = new(() =>
        {
            var state = Path.Combine(Path.GetTempPath(), "hopper-api-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(state);

            var postgres = new PostgreSqlBuilder("postgres:18.3")
                .WithCleanUp(true)
                .Build();
            postgres.StartAsync().GetAwaiter().GetResult();

            // Lazy has no teardown hook, and the container outliving the run would leak a container
            // per `dotnet test`. Ryuk (WithCleanUp) is the real safety net; this just makes the
            // common path tidy.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { postgres.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch { /* the process is going away regardless */ }
            };

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("ConnectionStrings__HopperDatabase", postgres.GetConnectionString());
            Environment.SetEnvironmentVariable("Blobs__Directory", Path.Combine(state, "blobs"));
            // Client tokens live in the Servers table now, so the suite pins the seeded "Default"
            // server's token instead of configuring an allow-list. Server B is added afterwards.
            Environment.SetEnvironmentVariable("Hopper__BootstrapClientToken", ClientToken);
            // Deliberately no Oidc:* : with no authority configured the JWT handler builds no
            // configuration manager, so an admin request fails validation locally and answers 401
            // without reaching for a network the test host does not have.
            Environment.SetEnvironmentVariable("Oidc__Authority", null);
            Environment.SetEnvironmentVariable("Oidc__InternalAuthority", null);

            var factory = new WebApplicationFactory<Program>();

            // Touching Services boots the host, which runs the migrator and the seeder - so by the
            // line after this one the "Default" server exists and carries ClientToken.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

                _serverAId = db.Servers.Single(s => s.Token == ClientToken).Id;

                var serverB = db.Servers.FirstOrDefault(s => s.Token == ClientTokenB);
                if (serverB is null)
                {
                    serverB = new Server { Name = "Server B", Slug = "server-b", Token = ClientTokenB };
                    db.Servers.Add(serverB);
                    db.SaveChanges();
                }

                _serverBId = serverB.Id;
            }

            return factory;
        });

        /// <summary>The running host's container, for seeding and for asserting on what a request
        /// actually persisted rather than on what the response said it did.</summary>
        public static IServiceProvider Services => Instance.Value.Services;

        /// <summary>A client with no credentials at all.</summary>
        public static HttpClient Anonymous() => Instance.Value.CreateClient();

        /// <summary>A client carrying server A's token, as the Forge locator would.</summary>
        public static HttpClient AsGameClient() => WithBearer(ClientToken);

        /// <summary>A client carrying server B's token - the other side of every isolation check.</summary>
        public static HttpClient AsGameClientB() => WithBearer(ClientTokenB);

        /// <summary>A client carrying an arbitrary bearer token.</summary>
        public static HttpClient WithBearer(string token)
        {
            var http = Instance.Value.CreateClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            return http;
        }
    }
}
