using HOPPER.API;
using HOPPER.API.Auth;
using HOPPER.API.Extensions;
using HOPPER.API.OpenApi;
using HOPPER.Application;
using HOPPER.Application.Exports;
using HOPPER.Application.Imports;
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

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

builder.Services.AddControllers();

builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

builder.Services.AddOpenApi(options => { options.AddDocumentTransformer<SecuritySchemeTransformer>(); });

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

builder.Services.AddHopperDatabase(builder.Configuration);
builder.Services.AddBlobs();

builder.Services.AddHostedService<ModIdBackfillService>();

builder.Services.AddLocatorJar();

builder.Services.AddPackImports();

builder.Services.AddBlobReclaim();

builder.Services.AddModrinth();

builder.Services.AddPackExports();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()

        .WithExposedHeaders("Content-Disposition", "X-Hopper-Export-Warnings")));

builder.Services.AddHopperAuthentication(builder.Configuration);
builder.Services.AddHopperAuthorization(builder.Configuration);

var app = builder.Build();

app.ApplyMigrations();
await app.ApplySeedsAsync();

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

    catch (ModrinthProjectNotFoundException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }

    catch (IncompatibleModException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }

    catch (ServerPlatformNotConfiguredException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }

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

    catch (LocatorTemplateMissingException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }

    catch (LocatorLoaderNotConfiguredException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (LocatorVariantNotAvailableException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }

    catch (ContentTooLargeException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
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

public partial class Program;
