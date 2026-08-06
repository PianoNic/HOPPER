# Developer setup

## Prerequisites

- .NET 10 SDK
- Bun
- Docker, for Postgres, the dev IdP and the test suite
- A JDK 17 or newer, only if you want to build the locator jar

## Run it

```bash
docker compose -f compose.dev.yml up -d          # Postgres on :5433, IdP on :58539
dotnet run --project src/HOPPER.API              # API on :5170
cd src/HOPPER.Frontend && bun install && bun start   # dashboard on :4200
```

Postgres is on **5433**, not 5432, so it cannot collide with one already running on the host. The built-in connection string default points there, so `dotnet run` needs no configuration.

This is the one setup with two origins, so it is also the only one that needs `Cors:AllowedOrigins`. That value, along with the `Oidc:*` values and a development client token, is already in `src/HOPPER.API/appsettings.Development.json`. Nothing to configure by hand.

## Tests

```bash
dotnet test                                      # needs Docker
cd src/HOPPER.Frontend && bun run test
```

The .NET suite starts a throwaway Postgres container through Testcontainers rather than testing against a different engine than production runs. Without Docker it fails at startup, which is the honest outcome: a green run against another engine would prove nothing about the database HOPPER ships on.

`dotnet test` needs `global.json`'s `test.runner` opt-in. TUnit is a Microsoft.Testing.Platform framework and the .NET 10 SDK dropped the VSTest bridge, so without it the build fails outright rather than reporting no tests.

It covers the fixed client wire format in both directions, cross-server isolation, blob sharing and orphan collection, the blob store's hashing and traversal rejection, and the client/admin auth split over HTTP against the real pipeline.

## The API client

Regenerate the typed Angular client after a contract change:

```bash
cd src/HOPPER.Frontend && bun run apigen      # reads http://localhost:5170/openapi/v1.json
```

The API has to be running for this.

## Migrations

```bash
dotnet ef migrations has-pending-model-changes -p src/HOPPER.Infrastructure -s src/HOPPER.API
dotnet ef migrations add <Name> -p src/HOPPER.Infrastructure -s src/HOPPER.API
```

`dotnet ef database update` is not needed. The API migrates at boot, so an upgrade is a restart.

## The locator jar

```bash
cd locator && ./gradlew build
```

Produces `locator/build/libs/hopper-1.0.0.jar`. That is the **template**: point `Hopper:LocatorTemplatePath` at it and `GET /api/servers/{id}/jar` serves per-server copies. The Dockerfile builds it in its own JDK stage, so a deployed HOPPER has one without anyone running Gradle.

No ForgeGradle and no reobf, because a locator never touches a Minecraft class and there is nothing to remap. Dependencies are pinned to what `fmlloader-1.20.1-47.1.3.pom` itself declares: forgespi 7.0.1, modlauncher 10.0.9.

Compilation uses `--release 17` rather than a toolchain, so the build runs on whatever JDK is installed instead of demanding one exact version. Warnings are errors (`-Xlint:all -Werror`): a mistake in a locator is a launch that silently does nothing.

The template must contain `META-INF/services/net.minecraftforge.forgespi.locating.IModLocator`, which is the one file that makes Forge lift it into the SERVICE layer, and `Automatic-Module-Name: hopper` in its manifest. `LocatorJarBuilder` refuses to patch a jar missing the services file rather than serve one that installs cleanly and then does nothing.

## Layout

```
locator/                    Forge mod locator (Java 17, Gradle)
src/HOPPER.Domain/          entities
src/HOPPER.Infrastructure/  EF Core, blob storage, jar patching
src/HOPPER.Application/     Mediator commands, queries, DTOs, pack import
src/HOPPER.API/             controllers, auth, OpenAPI, Dockerfile
src/HOPPER.Frontend/        Angular 21 dashboard
src/HOPPER.Tests/           TUnit
```

Application depends on Infrastructure and handlers inject the `DbContext` directly. There is no repository abstraction, deliberately, and adding one would not match the rest of the codebase.
