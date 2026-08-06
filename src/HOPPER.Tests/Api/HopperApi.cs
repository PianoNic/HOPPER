using HOPPER.Domain;
using HOPPER.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HOPPER.Tests.Api
{
    public static class HopperApi
    {
        public const string ClientToken = "test-client-token";

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

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { postgres.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch {  }
            };

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("ConnectionStrings__HopperDatabase", postgres.GetConnectionString());
            Environment.SetEnvironmentVariable("Blobs__Directory", Path.Combine(state, "blobs"));

            Environment.SetEnvironmentVariable("Hopper__BootstrapClientToken", ClientToken);

            Environment.SetEnvironmentVariable("Oidc__Authority", null);
            Environment.SetEnvironmentVariable("Oidc__InternalAuthority", null);

            var factory = new WebApplicationFactory<Program>();

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

        public static IServiceProvider Services => Instance.Value.Services;

        public static HttpClient Anonymous() => Instance.Value.CreateClient();

        public static HttpClient AsGameClient() => WithBearer(ClientToken);

        public static HttpClient AsGameClientB() => WithBearer(ClientTokenB);

        public static HttpClient WithBearer(string token)
        {
            var http = Instance.Value.CreateClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            return http;
        }
    }
}
