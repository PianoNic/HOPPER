using System.Security.Claims;
using HOPPER.API.Auth;
using HOPPER.API.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Tests.Api
{
    /// <summary>
    /// Authentication is not authorization, and the gap between them is the whole point of this file.
    /// A token can be perfectly genuine - correctly signed, from the configured issuer, not expired -
    /// and still belong to someone who was never given HOPPER at all, because .env.example invites
    /// operators to point Oidc:Authority at the realm they already run for everything else.
    ///
    /// These assert the policy and the token parameters directly rather than over HTTP, so they need
    /// no IdP: a suite that had to mint real tokens would need a signing key and a metadata endpoint,
    /// and would end up asserting the JWT library rather than HOPPER's own decisions.
    /// </summary>
    public class AdminAuthorizationTests
    {
        private static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
                .Build();

        /// <summary>A principal that authenticated through the OIDC scheme carrying the given roles.
        /// RoleClaimType is "roles" to match what ConfigureJwtBearer sets on the real handler.</summary>
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

        // ---- the admin role -------------------------------------------------------------------

        [Test]
        public async Task AdminPolicy_ByDefault_RequiresTheAdminRole()
        {
            // The blocker this file exists for. With authentication as the only requirement, every
            // account in a shared realm - a family member's login, a service account - could read
            // every server's client token and delete servers outright.
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
            // "has a role" is not "has this role". A realm hands out plenty of them.
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
            // The documented escape hatch for a realm HOPPER is the only client of. It stays
            // available; it is just no longer what an operator gets by not thinking about it.
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
            // A client token sits in a properties file inside a jar on machines nobody controls, and
            // its identity carries one claim: which server it is. So it fails the role requirement on
            // the only thing that matters here.
            //
            // Keeping it out when Oidc:AdminRole is CLEARED is a separate mechanism - the scheme
            // separation - and it is not visible from IAuthorizationService, which judges a principal
            // that middleware already built. AuthSplitTests asserts that half over real HTTP.
            var client = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClientTokenDefaults.ServerIdClaim, Guid.NewGuid().ToString())],
                ClientTokenDefaults.AuthenticationScheme));

            await Assert.That(await Allows(Config(), client)).IsFalse();
        }

        // ---- the audience ---------------------------------------------------------------------

        [Test]
        public async Task Audience_IsValidatedByDefault()
        {
            // Off, a token minted for ANY client on the same issuer opens HOPPER: the signature and
            // the issuer are all that would get checked, and both are shared across the realm.
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
        public async Task Audience_ExplicitListWins()
        {
            // For an issuer that stamps something other than the client id into aud.
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
            // The default map rewrites "roles" to the WS-* role URI on the way in, and RoleClaimType
            // is matched after that rewrite - so with mapping on, a token that plainly carries
            // hopper-admin fails the policy and every admin request 403s with the claim sitting right
            // there in the token. Same for "name". Verified against the bundled IdP: 403 before this
            // line, 200 after.
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

        // ---- blank configuration --------------------------------------------------------------

        [Test]
        public async Task Authority_BlankInternalAuthority_FallsBackToThePublicOne()
        {
            // Compose interpolates an unset variable to "" rather than omitting the key, and ?? does
            // not catch an empty string. Getting this wrong leaves the scheme with no metadata
            // address at all and fails every admin request for a reason nothing in the configuration
            // hints at.
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
            // A deployment that has not been told who to trust must trust nobody, rather than reach
            // for a default issuer.
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:Authority", ""), ("Oidc:InternalAuthority", "")));

            await Assert.That(options.MetadataAddress).IsNull();
        }

        [Test]
        public async Task Audience_NoClientId_LeavesTheListEmptyRatherThanCarryingABlank()
        {
            // A blank entry in ValidAudiences would be an audience an attacker could actually match.
            var options = new JwtBearerOptions();
            AuthExtensions.ConfigureJwtBearer(options, Config(("Oidc:ClientId", "")));

            await Assert.That(options.TokenValidationParameters.ValidAudiences).IsEmpty();
        }
    }
}
