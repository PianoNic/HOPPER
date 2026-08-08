# Developer setup

## Prerequisites

- .NET 10 SDK
- Bun, on the version `src/HOPPER.Frontend/package.json` declares in `packageManager`
- Docker, for Postgres, the dev IdP and the test suite
- JDK 21 through 24, only to build the locator templates

::: warning Match the declared Bun version
CI reads `packageManager` rather than pinning its own, and refuses a build whose Dockerfile
disagrees. Install with `bun upgrade --to <version>`. A different Bun rewrites `bun.lock` on
install, and CI uses `--frozen-lockfile`, so the failure shows up as a dependency problem rather
than version skew.
:::

## Run it

```bash
docker compose -f compose.dev.yml up -d          # Postgres on :5433, IdP on :58539
dotnet run --project src/HOPPER.API              # API on :5170
cd src/HOPPER.Frontend && bun install && bun start   # dashboard on :4200
```

Postgres is on **5433** so it cannot collide with one already running on the host. The default
connection string points there, so `dotnet run` needs no configuration.

This is the only setup with two origins, and so the only one needing `Cors:AllowedOrigins`. That and
the `Oidc:*` values are already in `src/HOPPER.API/appsettings.Development.json`.

## Tests

```bash
dotnet test                                      # needs Docker
cd src/HOPPER.Frontend && bun run test
```

The .NET suite starts a throwaway Postgres through Testcontainers, so it tests the engine production
runs on. Without Docker it fails at startup, which is the honest outcome.

It covers the fixed client wire format in both directions, cross-server isolation, blob sharing and
orphan collection, hashing and traversal rejection, the client/admin auth split over the real
pipeline, template selection per loader and Minecraft version, mod-id extraction, and pack import
for Modrinth, CurseForge, Prism and plain jar archives. CurseForge runs over a canned
`HttpMessageHandler`, so no API key and no network are needed.

::: warning `dotnet test` needs the `test.runner` opt-in in `global.json`
TUnit is a Microsoft.Testing.Platform framework and the .NET 10 SDK dropped the VSTest bridge.
Without it the build fails outright rather than reporting no tests.
:::

The client core has its own suite, which does not need Docker:

```bash
cd src/HOPPER.Locator && ./gradlew test
```

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

## The locator templates

```bash
cd src/HOPPER.Locator && ./gradlew templates
```

That produces `src/HOPPER.Locator/build/templates/`: seven jars under unversioned names. It is what
`Hopper:LocatorTemplateDirectory` points at, and `GET /api/servers/{id}/jar` picks one out of it by
the server's loader and Minecraft version before patching a per-server copy.

The Dockerfile runs the same task in its own JDK stage, so a deployed HOPPER has the set without
anyone running Gradle.

::: warning Build on JDK 21 through 24
Gradle 8.14.3 fails on JDK 25 with "Unsupported class file major version 69". The Dockerfile pins
`eclipse-temurin:21-jdk` for this reason.
:::

`./gradlew build` compiles and tests every module but does not collect them. `templates` is the task
that produces the layout the API reads.

### Which jar a server gets

| Server loader | Template |
| --- | --- |
| Forge, MC 1.12.x | `hopper-forge-1122.jar` |
| Forge, MC 1.13-1.16.5 | `hopper-forge-1165.jar` |
| Forge, MC 1.17-1.18.2 | `hopper-forge-1182.jar` |
| Forge, MC 1.19+ | `hopper-forge-modern.jar` |
| NeoForge | `hopper-neoforge.jar` |
| Fabric | `hopper-fabric.jar` |
| Quilt | `hopper-fabric.jar` |

Quilt is served the Fabric jar on purpose. Quilt runs Fabric mods through `StandardFabricPlugin`, so
the `preLaunch` entrypoint works unchanged. `hopper-quilt-plugin.jar` is built and tested, but it
hard-fails with a `ParseException` unless the player sets
`-Dloader.experimental.allow_loading_plugins=true`, and a launcher setting is the thing HOPPER
exists to avoid.

### Verifying it on a real client

No test here starts a loader, so nothing in `dotnet test` or `bun run test` proves the locator works.
`tools/locator-e2e/` does:

```bash
export HOPPER_E2E_TOKEN=...
python tools/locator-e2e/verify.py
```

It writes a throwaway Prism instance per adapter, serves it a configured jar, launches the client,
reads the log and closes it. Run it after touching anything under `src/HOPPER.Locator/`. Its
[README](https://github.com/PianoNic/HOPPER/blob/main/tools/locator-e2e/README.md) explains each
column of the result table.

::: info Everything else about the locator lives in one file
The modules and their version floors, what each adapter may touch, why `--release` rather than a
toolchain, why `-Werror` runs with exactly two lint categories off, how a template is recognised and
why the build is reproducible are all in [the locator build](locator.md). Read it before changing
anything in that directory, and put what you learn back into it.
:::

## Layout

```
src/HOPPER.Locator/         Gradle multi-module locator: hopper-core (Java 8) plus one adapter per loader
src/HOPPER.Domain/          entities
src/HOPPER.Infrastructure/  EF Core, blob storage, jar patching
src/HOPPER.Application/     Mediator commands, queries, DTOs, pack import
src/HOPPER.API/             controllers, auth, OpenAPI, Dockerfile
src/HOPPER.Frontend/        Angular 21 dashboard
src/HOPPER.Tests/           TUnit
```

Application depends on Infrastructure, and handlers inject the `DbContext` directly. There is no
repository abstraction, deliberately.
