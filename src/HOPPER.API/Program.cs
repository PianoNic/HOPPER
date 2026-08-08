using HOPPER.Domain;
using Microsoft.AspNetCore.HttpLogging;
using HOPPER.API;
using HOPPER.API.Auth;
using HOPPER.API.Extensions;
using HOPPER.API.OpenApi;
using HOPPER.Application;
using HOPPER.Application.Exports;
using HOPPER.Application.Imports;
using HOPPER.Application.Loaders;
using HOPPER.Application.Maintenance;
using HOPPER.Application.ModMetadata;
using HOPPER.Application.Modrinth;
using HOPPER.Application.Queries.Imports;
using HOPPER.Application.Command.Imports;
using HOPPER.Infrastructure.Extensions;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

System.Net.IPNetwork[] PrivateNetworks =
[
    new(System.Net.IPAddress.Parse("10.0.0.0"), 8),
    new(System.Net.IPAddress.Parse("172.16.0.0"), 12),
    new(System.Net.IPAddress.Parse("192.168.0.0"), 16),
    new(System.Net.IPAddress.Parse("fc00::"), 7),
];

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1;

    var section = builder.Configuration.GetSection("Hopper:TrustedProxies");
    var declared = (section.Value is { Length: > 0 } inline ? inline.Split(',') : section.Get<string[]>() ?? [])
        .Select(entry => (entry ?? string.Empty).Trim())
        .Where(entry => entry.Length > 0)
        .ToArray();

    // Unset means the shipped default: loopback plus the private ranges. ASP.NET on its own trusts
    // loopback only, which would stop believing a reverse proxy that runs as its own container - the
    // ordinary compose deployment - and quietly hand clients manifest URLs to the internal address.
    if (declared.Length == 0)
    {
        foreach (var network in PrivateNetworks)
            options.KnownIPNetworks.Add(network);

        return;
    }

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var entry in declared)
    {
        if (System.Net.IPNetwork.TryParse(entry, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
        else if (System.Net.IPAddress.TryParse(entry, out var address))
        {
            options.KnownProxies.Add(address);
        }
        else
        {
            throw new InvalidOperationException($"Hopper:TrustedProxies contains '{entry}', which is neither an IP address nor a CIDR network.");
        }
    }
});

builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

builder.Services.AddOpenApi(options => { options.AddDocumentTransformer<SecuritySchemeTransformer>(); });

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

builder.Services.AddHopperDatabase(builder.Configuration);
builder.Services.AddBlobs();

builder.Services.AddHostedService<ModMetadataBackfillService>();
builder.Services.AddHostedService<ModIconBackfillService>();

builder.Services.AddLocatorJar();

builder.Services.AddPackImports();

builder.Services.AddBlobReclaim();

builder.Services.AddModrinth();
builder.Services.AddLoaderVersions();

builder.Services.AddPackExports();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()

        .WithExposedHeaders("Content-Disposition", "X-Hopper-Export-Warnings")));

builder.Services.AddHopperHealthChecks();
builder.Services.AddHopperTelemetry(builder.Configuration);

builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

builder.Services.AddHttpClient(UserInfoClaims.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));

builder.Services.AddHopperAuthentication(builder.Configuration);
builder.Services.AddHopperAuthorization(builder.Configuration);

var app = builder.Build();

app.ApplyMigrations();

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

app.UseServedBytes();
app.UseHttpLogging();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

async Task Answer(HttpContext context, int status, Exception ex)
{
    app.Logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Information, ex,
        "{Method} {Path} answered {Status}: {Message}",
        context.Request.Method, context.Request.Path, status, ex.Message);

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = ex.Message });
}

// An unmapped exception must stay unmapped: StatusFor answering null makes the filter false, so it
// keeps travelling to the host's 500 handling instead of being dressed up as a client error.
static int? StatusFor(Exception ex) => ex switch
{
    DuplicateModFileNameException => StatusCodes.Status409Conflict,
    DuplicateModIdException => StatusCodes.Status409Conflict,
    DuplicateServerSlugException => StatusCodes.Status409Conflict,
    IncompatibleModException => StatusCodes.Status409Conflict,

    ServerNotFoundException => StatusCodes.Status404NotFound,
    ImportNotFoundException => StatusCodes.Status404NotFound,
    PendingModNotFoundException => StatusCodes.Status404NotFound,
    ModrinthProjectNotFoundException => StatusCodes.Status404NotFound,

    ModrinthApiException => StatusCodes.Status502BadGateway,
    LoaderVersionUnavailableException => StatusCodes.Status502BadGateway,

    LocatorTemplateMissingException => StatusCodes.Status503ServiceUnavailable,

    ContentTooLargeException => StatusCodes.Status413PayloadTooLarge,

    ServerPlatformNotConfiguredException => StatusCodes.Status400BadRequest,
    PackImportException => StatusCodes.Status400BadRequest,
    LocatorLoaderNotConfiguredException => StatusCodes.Status400BadRequest,
    LocatorVariantNotAvailableException => StatusCodes.Status400BadRequest,
    RuleViolationException => StatusCodes.Status400BadRequest,

    _ => null,
};

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (StatusFor(ex) is int status && !context.Response.HasStarted)
    {
        await Answer(context, status, ex);
    }
});

app.MapHopperHealthChecks();

app.MapControllers();

if (app.Environment.IsProduction())
    app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
