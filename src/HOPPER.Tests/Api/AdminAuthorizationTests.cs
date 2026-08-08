using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using HOPPER.API.Auth;
using HOPPER.API.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Tests.Api
{
    public class AdminAuthorizationTests
    {
        private static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
                .Build();

        private static ClaimsPrincipal OidcUser(params string[] roles) =>
            new(new ClaimsIdentity(
                [new Claim("name", "someone"), .. roles.Select(r => new Claim("roles", r))],
                authenticationType: JwtBearerDefaults.AuthenticationScheme,
                nameType: "name",
                roleType: "roles"));

        private static async Task<bool> Allows(IConfiguration configuration, ClaimsPrincipal user)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHopperAuthorization(configuration);

            var authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
            var policy = AuthExtensions.BuildAdminPolicy(configuration);

            return (await authorization.AuthorizeAsync(user, resource: null, policy)).Succeeded;
        }

        [Test]
        public async Task AdminPolicy_ByDefault_RequiresTheAdminRole()
        {
            await Assert.That(await Allows(Config(), OidcUser())).IsFalse();
        }

        [Test]
        public async Task AdminPolicy_UserCarryingTheAdminRole_IsAllowed()
        {
            await Assert.That(await Allows(Config(), OidcUser(AuthExtensions.DefaultAdminRole))).IsTrue();
        }

        [Test]
        public async Task AdminPolicy_UserWithSomeOtherRole_IsRejected()
        {
            await Assert.That(await Allows(Config(), OidcUser("grafana-viewer", "offline_access"))).IsFalse();
        }

        [Test]
        public async Task AdminPolicy_ConfiguredRoleName_IsHonoured()
        {
            var configuration = Config(("Oidc:AdminRole", "minecraft-ops"));

            await Assert.That(await Allows(configuration, OidcUser("minecraft-ops"))).IsTrue();
            await Assert.That(await Allows(configuration, OidcUser(AuthExtensions.DefaultAdminRole))).IsFalse();
        }

        [Test]
        [Arguments("")]
        [Arguments("   ")]
        public async Task AdminPolicy_ClearedRole_FallsBackToAnyAuthenticatedUser(string adminRole)
        {
            var configuration = Config(("Oidc:AdminRole", adminRole));

            await Assert.That(await Allows(configuration, OidcUser())).IsTrue();
        }

        [Test]
        public async Task AdminPolicy_AnonymousUser_IsRejectedEvenWithTheRoleCleared()
        {
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

            await Assert.That(await Allows(Config(("Oidc:AdminRole", "")), anonymous)).IsFalse();
            await Assert.That(await Allows(Config(), anonymous)).IsFalse();
        }

        [Test]
        public async Task AdminPolicy_ClientTokenPrincipal_CarriesNoRoleAndSoIsRejectedByDefault()
        {
            var client = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClientTokenDefaults.ServerIdClaim, Guid.NewGuid().ToString())],
                ClientTokenDefaults.AuthenticationScheme));

            await Assert.That(await Allows(Config(), client)).IsFalse();
        }

        [Test]
        public async Task Audience_IsValidatedByDefault()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:ClientId", "hopper")));

            await Assert.That(options.TokenValidationParameters.ValidateAudience).IsTrue();
        }

        [Test]
        public async Task Audience_DefaultsToTheConfiguredClientId()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:ClientId", "hopper")));

            await Assert.That(options.TokenValidationParameters.ValidAudiences).IsEquivalentTo(new[] { "hopper" });
        }

        [Test]
        public async Task Forbidden_NamesTheClaimsTheTokenActuallyCarried()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:RoleClaim", "groups")));

            var logs = new CollectingLoggerProvider();

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Warning));

            var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            http.Request.Path = "/api/servers";

            // The authenticated user lives here, and reading ForbiddenContext.Principal instead
            // reported a token with no claims at all.
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "u1"), new Claim("name", "someone"), new Claim("roles", "hopper-admin")],
                JwtBearerDefaults.AuthenticationScheme));

            await options.Events!.Forbidden(
                new ForbiddenContext(http, new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), options));

            var line = logs.Messages.Single();

            await Assert.That(line).Contains("/api/servers");
            await Assert.That(line).Contains("groups");
            await Assert.That(line).Contains("sub");
            await Assert.That(line).Contains("roles");
        }

        private sealed class CollectingLoggerProvider : ILoggerProvider
        {
            public List<string> Messages { get; } = [];

            public ILogger CreateLogger(string categoryName) => new Collector(Messages);

            public void Dispose()
            {
            }

            private sealed class Collector(List<string> messages) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                    Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
            }
        }

        [Test]
        public async Task RoleClaim_DefaultsToRoles()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config());

            await Assert.That(options.TokenValidationParameters.RoleClaimType).IsEqualTo("roles");
        }

        [Test]
        public async Task RoleClaim_CanBeTheGroupsClaimAnIdpAlreadyPublishes()
        {
            // Pocket ID, Authentik and Keycloak publish membership as `groups`. Reading `roles`
            // there refuses every admin request while the token itself is perfectly valid.
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:RoleClaim", "groups")));

            await Assert.That(options.TokenValidationParameters.RoleClaimType).IsEqualTo("groups");
        }

        [Test]
        public async Task AdminPolicy_MatchesAGroupWhenTheRoleClaimPointsAtGroups()
        {
            var configuration = Config(("Oidc:RoleClaim", "groups"), ("Oidc:AdminRole", "private"));

            var member = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("name", "someone"), new Claim("groups", "private")],
                authenticationType: JwtBearerDefaults.AuthenticationScheme,
                nameType: "name",
                roleType: AuthExtensions.RoleClaim(configuration)));

            var outsider = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("name", "someone"), new Claim("groups", "community")],
                authenticationType: JwtBearerDefaults.AuthenticationScheme,
                nameType: "name",
                roleType: AuthExtensions.RoleClaim(configuration)));

            await Assert.That(await Allows(configuration, member)).IsTrue();
            await Assert.That(await Allows(configuration, outsider)).IsFalse();
        }

        [Test]
        public async Task RoleClaim_BlankIsTreatedAsUnsetRatherThanAsAnEmptyClaimName()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:RoleClaim", "  ")));

            await Assert.That(options.TokenValidationParameters.RoleClaimType).IsEqualTo("roles");
        }

        [Test]
        public async Task Audience_ExplicitListWins()
        {
            var configuration = Config(
                ("Oidc:ClientId", "hopper"),
                ("Oidc:ValidAudiences:0", "hopper-api"),
                ("Oidc:ValidAudiences:1", "account"));

            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, configuration);

            await Assert.That(options.TokenValidationParameters.ValidAudiences)
                .IsEquivalentTo(new[] { "hopper-api", "account" });
        }

        [Test]
        public async Task InboundClaims_AreNotRemapped_OrTheRoleRequirementSilentlyNeverMatches()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:ClientId", "hopper")));

            await Assert.That(options.MapInboundClaims).IsFalse();
            await Assert.That(options.TokenValidationParameters.RoleClaimType).IsEqualTo("roles");
            await Assert.That(options.TokenValidationParameters.NameClaimType).IsEqualTo("name");
        }

        [Test]
        public async Task Audience_CanBeTurnedOffExplicitly()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:ValidateAudience", "false")));

            await Assert.That(options.TokenValidationParameters.ValidateAudience).IsFalse();
        }

        [Test]
        public async Task Authority_BlankInternalAuthority_FallsBackToThePublicOne()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(
                ("Oidc:Authority", "https://id.example.com/realms/hopper"),
                ("Oidc:InternalAuthority", "")));

            await Assert.That(options.MetadataAddress)
                .IsEqualTo("https://id.example.com/realms/hopper/.well-known/openid-configuration");
            await Assert.That(options.TokenValidationParameters.ValidIssuer)
                .IsEqualTo("https://id.example.com/realms/hopper");
        }

        [Test]
        public async Task Authority_BlankEverywhere_ConfiguresNoMetadataAddress()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:Authority", ""), ("Oidc:InternalAuthority", "")));

            await Assert.That(options.MetadataAddress).IsNull();
        }

        [Test]
        public async Task Audience_NoClientId_LeavesTheListEmptyRatherThanCarryingABlank()
        {
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:ClientId", "")));

            await Assert.That(options.TokenValidationParameters.ValidAudiences).IsEmpty();
        }
    }
}
