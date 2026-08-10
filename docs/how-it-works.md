# How HOPPER works

## No restart

HOPPER is a **mod locator**, not a mod.

Forge's `ModDirTransformerDiscoverer` walks `mods/` and lifts any jar providing
`net.minecraftforge.forgespi.locating.IModLocator` into the SERVICE layer. That layer is built
**before** `ModDiscoverer` scans for mods. HOPPER downloads the required set from there, and FML
picks those jars up in the same launch.

That is why there is no restart, and why it does not care which launcher started the game.

A normal `@Mod` runs after the jar scan. On Windows the open jars cannot be replaced, so applying
files needs a restart and a second process. HOPPER avoids that by never writing into `mods/`.
Downloads land in `hoppermods/`, a directory it owns outright. Fabric is the one exception, because
it is the one loader that will not read anything else - see [Fabric](#fabric) below.

## The generated jar

A jar is a zip. HOPPER ships one template jar per loader generation and, on download, copies the
one this server's loader needs and writes a single extra entry into it:

```properties
# hopper-server.properties
serverId=<guid>
manifestUrl=https://hopper.example.com/api/manifest
token=<this server's client token>
```

No JDK runs at request time. The template is built once in the Dockerfile's JDK stage and patched with `System.IO.Compression`.

Settings merge **per key**, jar first:

| key | jar | `config/hopper.properties` |
| --- | --- | --- |
| `serverId` | written on download | fallback |
| `manifestUrl` | written on download | fallback |
| `token` | written on download | fallback |
| `enabled` | never written | **the only source** |

`enabled` is left out of the jar on purpose, so `enabled=false` still stops a downloaded jar from
syncing. The player keeps a local kill switch on a jar that otherwise configures itself.

## The API the client talks to

```
GET  /api/manifest              the mod list, for the token's server
GET  /api/blobs/{sha256}        the jar bytes
POST /api/clients/report        what this client ended up with
GET  /api/servers/{id}/jar      the jar with all of the above baked in
```

None of the three client endpoints carries a server segment. The server is resolved from the bearer
token instead.

::: info Why no server segment
The Java client derives its report URL from its manifest URL. A segment on one would silently move
the other.
:::

```json
{
  "mods": [
    {
      "file": "jei-1.20.1-15.2.0.27.jar",
      "url": "https://hopper.example.com/api/blobs/...",
      "sha256": "...",
      "size": 1234567
    }
  ]
}
```

## Sync behaviour

- A file is downloaded only if it is missing or its sha256 does not match.
- Anything in `hoppermods/` the manifest no longer lists is disposed of by where it came from. A jar
  HOPPER downloaded is deleted, since the server still has it. Anything a person put there is moved
  to `hoppermods/parked/` with a `.parked` suffix, where no loader sees it, and deleted three days
  later with a line in the log.
- HOPPER tells the two apart using `hoppermods/downloaded`, written after every sync. Delete that
  file and HOPPER forgets the claim: everything stale is parked from then on, which is the safe direction
  to be wrong in.
- `client-id`, `downloaded`, `mods-mirror.txt` and `parked/` are HOPPER's own bookkeeping and are
  never swept. A leftover `.part` from an interrupted download is.
- A download whose hash does not match is discarded, not installed.
- Filenames are rejected if they contain a path separator, `..`, a leading dot, or do not end in `.jar`.
- A failed report never fails the sync, and a failed sync never blocks the launch.

## Loader coverage

Any loader with a hook that runs *before* mod discovery can do the no-restart trick. The hook
differs in each, so one jar cannot serve them all:

| Loader | Same launch | Side detected from | Hook |
| --- | --- | --- | --- |
| Forge 1.14.4 to 1.16.5 | yes | `Environment.Keys.DIST` | `forgespi.locating.IModLocator` (SPI 3.2 and below), `scanMods()` returns `List<IModFile>` |
| Forge 1.17.1 to 1.18.2 | yes | `Environment.Keys.DIST` | same class name, SPI 4.0, still `List<IModFile>` but no `findPath` or `findManifest` |
| Forge 1.19 to 26.2 | yes | `Environment.Keys.DIST` | same class name again, SPI 6.0+, `extends IModProvider` and `scanMods()` returns `List<ModFileOrException>` |
| NeoForge 21.1 and newer | yes | `FMLEnvironment.dist` | `neoforgespi.locating.IModFileCandidateLocator`, `findCandidates(ILaunchContext, IDiscoveryPipeline)` |
| Quilt | **no**, opt-in mirror | `MinecraftQuiltLoader.getEnvironmentType()` | `org.quiltmc.loader.api.plugin.QuiltLoaderPlugin`, see below |
| Fabric | **no**, opt-in mirror | `FabricLoader.getEnvironmentType()` | see below |
| Forge 1.12.x | yes | `FMLLaunchHandler.side()` | `IFMLLoadingPlugin` coremod, a separate codebase rather than an adapter |

Forge keeps the same class name across all three generations while changing the signature, so they
are binary incompatible with each other. One class cannot implement more than one of them, which is
why there is an adapter per generation rather than a jar with three branches.

Only the adapter is loader-specific. `Syncer` imports nothing from any loader, which is what makes
adding one cheap.

## Sides

The same jar runs on a player's machine and on the server they connect to. "Dedicated server"
below means that second one: the always-on install everybody joins, rather than a world someone
opened to LAN. The adapter asks its loader which side it is on and requests the matching set.

Every mod carries one of three values. `Both` is the default, so a server that has never classified
anything behaves as it always did:

| Side | Goes to players | Goes to the dedicated server |
| --- | --- | --- |
| `Both` | yes | yes |
| `Client only` | yes | no |
| `Server only` | no | yes |

A client-only mod on a dedicated server is at best pointless and at worst a crash on boot. JEI,
Xaero's Minimap, Sodium and Iris all have no business there.

Mechanically it is one query parameter. A client asks for `/api/manifest` and gets `Both` plus
`Client only`. A dedicated server asks for `/api/manifest?side=server` and gets `Both` plus
`Server only`. The blob endpoint applies the same rule, so a jar cannot be fetched by hash by a side
it was never sent to.

HOPPER fills the side in wherever the source knows it:

- The `env` object on an `.mrpack` entry
- The `client-overrides/` and `server-overrides/` folders
- Modrinth's `client_side` and `server_side`
- Failing those, the `environment` field in the jar's own `fabric.mod.json` or `quilt.mod.json`

Prism and CurseForge packs carry no side, so they fall back to the jar. Anything with no signal
stays `Both`. Correct it on the Mods page, which sets any number of rows at once.

Exporting reverses the same knowledge, and the three formats differ:

| Format | Sides |
| --- | --- |
| `.mrpack` | Records a side per file, so it survives a round trip unchanged |
| CurseForge | Carries no side and a server operator installs it too, so it ships both sets |
| Prism | One machine's game directory, in practice a client, so it gets what a player gets |

### Fabric

Fabric has no public pre-discovery hook. Its only lever is the `fabric.addMods` system property,
which means a JVM argument, which means a launcher setting. The CurseForge app does not expose one.
Everything else in Fabric's discovery lives under the internal `net.fabricmc.loader.impl`.

So Fabric gets the honest version: sync from the `preLaunch` entrypoint, and tell the player a
restart is needed when something changed. AutoModpack does the same, for the same reason.

Fabric also reads nothing but `mods/`, so a sync into `hoppermods/` on its own would load nothing
ever. The Fabric jar therefore copies what it downloaded into `mods/`, and that is the one place in
HOPPER that writes into a directory the player owns - which is why it does not do it unasked:

```properties
# config/hopper.properties
fabricMirrorMods=true
```

Left unset, the sync still runs and the log says plainly that nothing will load and which line to
add. Set, the jar removes only the filenames it recorded in `hoppermods/mods-mirror.txt`, so a mod
you put in `mods/` yourself is never touched. A Quilt install is served the Fabric jar, so it needs
the same line.

### Quilt

Quilt has the right hook and a declaration it refuses to read. `V1ModMetadataImpl` throws a
`ParseException` the moment it sees the `experimental_quilt_loader_plugin` key while
`-Dloader.experimental.allow_loading_plugins=true` is unset. That is a hard failure, not a
degradation, and setting the flag only moves it: parsing then succeeds and Quilt's own plugin
classloader refuses the classes instead. Neither way boots a client.

So a Quilt server is served `hopper-fabric.jar`. Quilt runs Fabric mods through
`StandardFabricPlugin`, the `preLaunch` entrypoint works unchanged, and the player gets the Fabric
promise: it works, it needs a restart.

The plugin jar is built and tested as `hopper-quilt-plugin.jar` all the same, and nothing serves it.
It is correct code waiting on Quilt. [The measurements](/locator#hopper-quilt).

## Limits

- HOPPER cannot update its own jar; it is open while it runs.
- Someone places that first jar by hand. Nothing can reach a client that has not run anything yet.
- One token per server, shared by every client, in plain text inside a jar on machines you do not
  control. It gates read access to that server's mod set. Rotating it invalidates every jar already
  handed out.
- `username` is self-reported and unverified. It labels a row in the dashboard. It is not an
  identity.
