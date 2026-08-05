using HOPPER.API;
using HOPPER.API.Auth;
using HOPPER.API.Extensions;
using HOPPER.API.OpenApi;
using HOPPER.Application;
using HOPPER.Infrastructure.Extensions;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// The manifest hands clients absolute URLs built from the incoming request, so behind a reverse
// proxy the scheme and host have to come from the forwarded headers or every client would be told to
// download over http:// from an internal hostname it cannot reach. The known-proxy lists are cleared
// because the proxy sits at an address we cannot know ahead of time (a container network, Cloudflare,
// a home router) — acceptable here because HOPPER is expected to run behind a proxy it owns.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

builder.Services.AddControllers();

// [RequestSizeLimit] raises Kestrel's cap but not the multipart section cap, which is enforced
// separately while binding IFormFile and defaults to 128 MB.
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 512L * 1024 * 1024);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

builder.Services.AddOpenApi(options => { options.AddDocumentTransformer<SecuritySchemeTransformer>(); });

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

builder.Services.AddHopperDatabase(builder.Configuration);
builder.Services.AddBlobs();

// Defaults to no cross-origin allowlist. A deployment that serves the SPA from the API's wwwroot
// (src/HOPPER.API/Dockerfile copies it there) is same-origin and needs none. The split dev setup —
// `bun start` on :4200 against `dotnet run` on :5170 — is cross-origin, and sets
// http://localhost:4200 explicitly in appsettings.Development.json. .env.example documents the key
// for a deployment that does serve the dashboard from a separate origin.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var publicAuthority = builder.Configuration["Oidc:Authority"];
        var internalAuthority = builder.Configuration["Oidc:InternalAuthority"] ?? publicAuthority;

        // In Docker the browser reaches the IdP on a published port while the API reaches it on the
        // compose network, so metadata is fetched from the internal URL but the issuer inside the
        // token is validated against the public one.
        if (!string.IsNullOrWhiteSpace(internalAuthority))
        {
            options.MetadataAddress = $"{internalAuthority.TrimEnd('/')}/.well-known/openid-configuration";
            options.TokenValidationParameters.ValidIssuer = publicAuthority;
        }

        options.RequireHttpsMetadata = builder.Configuration.GetValue("Oidc:RequireHttpsMetadata", true);
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.TokenValidationParameters.ValidateAudience = false;
    })
    .AddClientToken();

builder.Services.AddAuthorization(options =>
{
    // Secure by default: an endpoint is protected by doing nothing. Client endpoints carry an
    // explicit [Authorize(AuthenticationSchemes = "ClientToken")], which replaces this policy for
    // those actions, so an OIDC token never opens the manifest and a client token never opens the
    // admin surface.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

var app = builder.Build();

// Migrations run at boot rather than from the CLI, so an upgrade is just a restart.
app.ApplyMigrations();
await app.ApplySeedsAsync();

// First in the pipeline: everything downstream that reads Request.Scheme or Request.Host — the
// manifest URLs above all — has to see the client-facing values, not the proxy's.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options =>
    {
        options
            .AddPreferredSecuritySchemes("OAuth2")
            .AddAuthorizationCodeFlow("OAuth2", flow =>
            {
                flow.ClientId = builder.Configuration["Oidc:ClientId"];
                flow.Pkce = Pkce.Sha256;
                flow.SelectedScopes = ["openid", "profile", "email", "roles"];
            });
    }).AllowAnonymous();
}

app.UseStaticFiles();

if (app.Environment.IsProduction())
    app.UseSpaStaticFiles();

app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Cross-cutting exception -> status mapping as a terminal inline middleware rather than a filter,
// matching KRINT. Guarded on HasStarted so a blob response that is already streaming is never
// corrupted by a late attempt to write an error body over it.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (DuplicateModFileNameException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (ArgumentException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapControllers();

if (app.Environment.IsProduction())
    app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

// Exposed so Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<Program> can find this
// assembly's entry point. Top-level statements compile to an internal Program class, which the
// factory cannot reference; this is the documented way to widen it.
public partial class Program;
