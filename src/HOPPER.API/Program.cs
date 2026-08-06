using HOPPER.API;
using HOPPER.API.Auth;
using HOPPER.API.Extensions;
using HOPPER.API.OpenApi;
using HOPPER.Application;
using HOPPER.Application.Exports;
using HOPPER.Application.Imports;
using HOPPER.Application.ModMetadata;
using HOPPER.Application.Modrinth;
using HOPPER.Application.Queries.Imports;
using HOPPER.Application.Command.Imports;
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
// a home router) - acceptable here because HOPPER is expected to run behind a proxy it owns.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

builder.Services.AddControllers();

// [RequestSizeLimit] raises Kestrel's cap but not the multipart section cap, which is enforced
// separately while binding IFormFile and defaults to 128 MB. 2 GB rather than the old 512 MB because
// a modpack export is a single multipart part of that size; Hopper:MaxImportBytes is the limit that
// actually decides what an import will accept, and it is counted as the bytes arrive.
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

builder.Services.AddOpenApi(options => { options.AddDocumentTransformer<SecuritySchemeTransformer>(); });

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

builder.Services.AddHopperDatabase(builder.Configuration);
builder.Services.AddBlobs();

// Reads the mod ids out of jars that were stored before mod ids existed as a concept. Every store
// path extracts them inline from now on; this is what makes the client's de-duplication work on a
// server that has been running since before the column did. It is hosted rather than run at boot so
// it starts after the migrator and never delays startup, and one failed pass is never fatal.
builder.Services.AddHostedService<ModIdBackfillService>();

// Patches a copy of the shipped template jar per download. No JDK at runtime - the toolchain lives
// in the Dockerfile's locator stage and never reaches the running image.
builder.Services.AddLocatorJar();

// Queue, staging directory, HTTP client and the single background worker that drains them.
builder.Services.AddPackImports();

// The mod browser: an HTTP client carrying the descriptive User-Agent Modrinth require, a
// process-wide token bucket for their 300-per-minute limit, and the dependency resolver.
builder.Services.AddModrinth();

// The three pack writers behind one interface, selected by format.
builder.Services.AddPackExports();

// Defaults to no cross-origin allowlist. A deployment that serves the SPA from the API's wwwroot
// (src/HOPPER.API/Dockerfile copies it there) is same-origin and needs none. The split dev setup -
// `bun start` on :4200 against `dotnet run` on :5170 - is cross-origin, and sets
// http://localhost:4200 explicitly in appsettings.Development.json. .env.example documents the key
// for a deployment that does serve the dashboard from a separate origin.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Response headers are invisible to cross-origin JavaScript unless they are named here, and
        // both of these are read by the dashboard: Content-Disposition carries the exported pack's
        // filename, and the warnings header is how a pack that skipped a mod with a missing blob says
        // so without failing the download.
        .WithExposedHeaders("Content-Disposition", "X-Hopper-Export-Warnings")));

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

// First in the pipeline: everything downstream that reads Request.Scheme or Request.Host - the
// manifest URLs above all - has to see the client-facing values, not the proxy's.
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
    catch (DuplicateServerSlugException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (ServerNotFoundException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (ImportNotFoundException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (PendingModNotFoundException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    // A stale link in the dashboard, not a fault: a project Modrinth no longer has should read as
    // 404 rather than as "Modrinth is broken".
    catch (ModrinthProjectNotFoundException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    // 409: the request named a set that cannot load together. Nothing was written, and the message
    // names both mods, so retrying without one of them is an obvious next step.
    catch (IncompatibleModException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    // 400: a server whose Minecraft version or loader the admin has not filled in yet. Not a fault -
    // the message names exactly which fields to set.
    catch (ServerPlatformNotConfiguredException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    // 502, not 500: Modrinth being down, rate-limiting us or answering with something unreadable is
    // an upstream failing, not HOPPER malfunctioning. The message names Modrinth so an admin does not
    // file a HOPPER bug for someone else's outage. This catch must sit above the ArgumentException
    // one but is otherwise order-independent.
    catch (ModrinthApiException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (PackImportException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    // 503, not 500: the template jar being absent is a deployment that has not finished rather than a
    // request that went wrong, and the message names the configuration key that fixes it. This can
    // only ever fire before the response starts, because the builder completes the archive in memory.
    catch (LocatorTemplateMissingException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
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
