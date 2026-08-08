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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1;

    var section = builder.Configuration.GetSection("Hopper:TrustedProxies");
    var declared = (section.Value is { Length: > 0 } inline ? inline.Split(',') : section.Get<string[]>() ?? [])
        .Select(entry => (entry ?? string.Empty).Trim())
        .Where(entry => entry.Length > 0)
        .ToArray();

    if (declared.Length == 0)
    {
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

builder.Services.AddHostedService<ModIdBackfillService>();
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

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (DuplicateModFileNameException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status409Conflict, ex);
    }
    catch (DuplicateServerSlugException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status409Conflict, ex);
    }
    catch (ServerNotFoundException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status404NotFound, ex);
    }
    catch (ImportNotFoundException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status404NotFound, ex);
    }
    catch (PendingModNotFoundException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status404NotFound, ex);
    }

    catch (ModrinthProjectNotFoundException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status404NotFound, ex);
    }

    catch (IncompatibleModException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status409Conflict, ex);
    }

    catch (ServerPlatformNotConfiguredException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status400BadRequest, ex);
    }

    catch (ModrinthApiException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status502BadGateway, ex);
    }
    catch (LoaderVersionUnavailableException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status502BadGateway, ex);
    }
    catch (PackImportException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status400BadRequest, ex);
    }

    catch (LocatorTemplateMissingException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status503ServiceUnavailable, ex);
    }

    catch (LocatorLoaderNotConfiguredException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status400BadRequest, ex);
    }
    catch (LocatorVariantNotAvailableException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status400BadRequest, ex);
    }

    catch (ContentTooLargeException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status413PayloadTooLarge, ex);
    }
    catch (ArgumentException ex) when (!context.Response.HasStarted)
    {
        await Answer(context, StatusCodes.Status400BadRequest, ex);
    }
});

app.MapHopperHealthChecks();

app.MapControllers();

if (app.Environment.IsProduction())
    app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
