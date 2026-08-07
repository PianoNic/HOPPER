# Developer setup

## Prerequisites

- .NET 10 SDK
- Bun, on the version `src/HOPPER.Frontend/package.json` declares in `packageManager`. CI reads that
  field rather than pinning its own, and refuses a build whose Dockerfile disagrees with it, so the
  three cannot drift apart. Install it with `bun upgrade --to <version>`. A different Bun may rewrite
  `bun.lock` on `bun install`, and CI installs with `--frozen-lockfile`, so a rewritten lockfile
  fails there as what looks like a dependency problem rather than a version-skew one.
- Docker, for Postgres, the dev IdP and the test suite
- JDK 21 through 24, only if you want to build the locator templates. Gradle 8.14.3 refuses to start on JDK 25 with "Unsupported class file major version 69", which is why the Dockerfile pins `eclipse-temurin:21-jdk`.

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

It covers the fixed client wire format in both directions, cross-server isolation, blob sharing and orphan collection, the blob store's hashing and traversal rejection, the client/admin auth split over HTTP against the real pipeline, locator template selection per loader and Minecraft version, mod-id extraction, and pack import for Modrinth, CurseForge with and without an API key, Prism and plain jar archives.

The CurseForge import path is exercised over a canned `HttpMessageHandler` (`src/HOPPER.Tests/Imports/CannedHttp.cs`), so the with-key branch, the 100-id batching, the Modrinth sha1 mirror bridge and the author-blocked case all run without a live `CurseForge:ApiKey` and without a network.

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

Produces `src/HOPPER.Locator/build/templates/`, seven jars under unversioned names. That directory is what `Hopper:LocatorTemplateDirectory` points at, and `GET /api/servers/{id}/jar` picks one out of it by the server's loader and Minecraft version before patching a per-server copy. The Dockerfile runs the same task in its own JDK stage, so a deployed HOPPER has the set without anyone running Gradle.

`./gradlew build` compiles and tests every module but does not collect them; `templates` is the task that produces the layout the API reads. Run either on JDK 21 through 24.

### The modules

Eight Gradle modules, not one jar. `hopper-core` holds the syncer and is embedded verbatim into each adapter, because a loader resolves one jar out of `mods/` and nothing else.

| Module | `--release` | Oldest Minecraft it claims | Loader dependency, all `compileOnly` |
| --- | --- | --- | --- |
| `hopper-core` | 8 | n/a, no loader imports at all | none |
| `hopper-forge-1122` | 8 | Forge 1.12.2 | `net.minecraftforge:forge:1.12.2-14.23.5.2864:universal`, `net.minecraft:launchwrapper:1.12` |
| `hopper-forge-1165` | 8 | Forge 1.14.4 | `forgespi:1.5.0`, `cpw.mods:modlauncher:8.1.3` |
| `hopper-forge-1182` | 16 | Forge 1.17.1 | `forgespi:4.0.9`, `cpw.mods:modlauncher:9.0.7` |
| `hopper-forge-modern` | 17 | Forge 1.19 | `forgespi:6.0.0`, `cpw.mods:modlauncher:10.0.1` |
| `hopper-neoforge` | 21 | NeoForge 21.1 | `net.neoforged.fancymodloader:loader:4.0.24` |
| `hopper-fabric` | 8 | Fabric 1.14 | `net.fabricmc:fabric-loader:0.19.3` |
| `hopper-quilt` | 8 | Quilt | `org.quiltmc:quilt-loader:0.30.0` |

Each module pins only what its own loader generation ships, and every pin is the **oldest** artifact in the claimed range, so an ordinary build is what proves the floor still holds. There is no global pin: `fmlloader`'s dependency set describes `hopper-forge-modern` and nothing else.

Every dependency is `compileOnly` because every one of them is already on the classpath when the locator runs. No ForgeGradle and no reobf, because a locator never touches a Minecraft class and there is nothing to remap.

Compilation uses `--release` rather than a toolchain, so the build runs on whatever JDK is installed instead of demanding eight exact ones. Each module targets the oldest Minecraft it claims - the core is 8 because 1.16.5 and older run on Java 8, and that is what lets every adapter embed it. Warnings are errors (`-Xlint:all -Werror`): a mistake in a locator is a launch that silently does nothing.

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
| Quilt, with `-Dloader.experimental.allow_loading_plugins=true` | `hopper-quilt-plugin.jar`, opt-in |

Quilt is served the Fabric jar by default, on purpose. Quilt runs Fabric mods through `StandardFabricPlugin`, so the `preLaunch` entrypoint works unchanged, while `hopper-quilt-plugin.jar` hard-fails with a `ParseException` unless the player adds that JVM argument - a launcher setting, which is the thing HOPPER exists to avoid. It is built, tested and shipped as `hopper-quilt-plugin.jar`; it is just never the default. A Quilt player who has set the flag gets same-launch loading from:

```
GET /api/servers/{id}/jar?variant=quilt-plugin
```

The variant is Quilt-only. Asking for it on any other loader is a 400 that names the flag, rather than a jar the loader would refuse to parse.

### How a template is recognised

Each template carries one marker entry, and `LocatorJarBuilder` refuses to patch a jar that is missing its own:

| Template | Marker entry |
| --- | --- |
| `hopper-forge-1122.jar` | `ch/pianonic/hopper/HopperCoreMod.class` |
| `hopper-forge-1165.jar`, `hopper-forge-1182.jar`, `hopper-forge-modern.jar` | `META-INF/services/net.minecraftforge.forgespi.locating.IModLocator` |
| `hopper-neoforge.jar` | `META-INF/services/net.neoforged.neoforgespi.locating.IModFileCandidateLocator` |
| `hopper-fabric.jar` | `fabric.mod.json` |
| `hopper-quilt-plugin.jar` | `quilt.mod.json` |

The failure is a 503 naming the file, rather than a jar that installs cleanly and then does nothing. `Automatic-Module-Name: hopper` is set by `hopper-forge-1182`, `hopper-forge-modern` and `hopper-neoforge` only, where the jar becomes a JPMS module on the SERVICE layer; nothing checks for it, so it is not a marker.

### Reproducibility

Two clean builds of the same sources produce the same bytes, so "the template did not change" is a checkable statement. Verify it in thirty seconds:

```bash
cd src/HOPPER.Locator
./gradlew --no-daemon clean templates && sha256sum build/templates/*.jar | sort -k2 > /tmp/hopper-a
./gradlew --no-daemon clean templates && sha256sum build/templates/*.jar | sort -k2 > /tmp/hopper-b
diff /tmp/hopper-a /tmp/hopper-b && echo reproducible
```

`preserveFileTimestamps = false` and `reproducibleFileOrder = true` in the root `subprojects` block are what make that hold, against Gradle's defaults of stamping each zip entry with its source file's mtime and emitting entries in filesystem order. The guarantee is "same sources, same JDK, same OS" - a different javac writes different class files, and permission bits differ between a Windows host and the Docker JDK stage.

The per-server jar the endpoint serves is not byte-identical between two downloads, and is not meant to be: `hopper-server.properties` is generated per server and stamped with the current time.

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

Application depends on Infrastructure and handlers inject the `DbContext` directly. There is no repository abstraction, deliberately, and adding one would not match the rest of the codebase.
