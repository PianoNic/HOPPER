# How HOPPER works

## No restart

HOPPER is a **mod locator**, not a mod. Forge's `ModDirTransformerDiscoverer` walks `mods/`, reads each jar's module descriptor, and lifts any jar that *provides* `net.minecraftforge.forgespi.locating.IModLocator` into the SERVICE layer. That layer is built **before** `ModDiscoverer` scans for mods.

So HOPPER downloads the required set, and FML then picks those jars up in the same launch. That is why there is no restart, and why it does not care which launcher started the game.

A normal `@Mod` runs after the jar scan. On Windows the open jars cannot be replaced, which forces a restart plus a second process to apply the files. HOPPER avoids that entirely by never writing into `mods/`: downloads land in `hoppermods/`, a directory it owns outright, so a player's own mods are never touched either.

## The generated jar

A jar is a zip. HOPPER ships one template jar and, on download, copies it and writes a single extra entry into it:

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

`enabled` is deliberately left out of the jar, so `enabled=false` still stops a downloaded jar from syncing. The player keeps a local kill switch on a jar that otherwise configures itself.

## The API the client talks to

```
GET  /api/manifest              the mod list, for the token's server
GET  /api/blobs/{sha256}        the jar bytes
POST /api/clients/report        what this client ended up with
GET  /api/servers/{id}/jar      the jar with all of the above baked in
```

None of the three client endpoints carries a server segment, and that is deliberate: the Java client derives its report URL from its manifest URL, so a segment on one would silently move the other. The server is resolved from the bearer token instead.

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
  HOPPER downloaded is deleted, because the server still has it and the next sync fetches it again.
  Anything else - a jar you dropped in by hand, or one HOPPER moved out of your `mods/` folder - is
  moved to `hoppermods/replaced/` with a `.replaced` suffix, where no loader sees it and nothing is
  destroyed. Deleting a file a person put there is the one thing HOPPER will not do, and parking
  everything instead would grow that folder by the size of the pack on every update.
- HOPPER knows which is which from `hoppermods/downloaded`, the list it writes after every sync.
  Delete that file and HOPPER forgets the claim: from then on everything stale is parked, which is
  the safe direction to be wrong in.
- `client-id`, `downloaded`, `mods-mirror.txt` and `replaced/` are HOPPER's own bookkeeping and are
  never swept. A leftover `.part` from an interrupted download is.
- A download whose hash does not match is discarded, not installed.
- Filenames are rejected if they contain a path separator, `..`, a leading dot, or do not end in `.jar`.
- A failed report never fails the sync, and a failed sync never blocks the launch.

## Loader coverage

Every loader that exposes a hook running *before* mod discovery can do the no-restart trick. The hook is different in each of them, so one jar cannot serve them all:

| Loader | Same launch | Side detected from | Hook |
| --- | --- | --- | --- |
| Forge 1.16.x | yes | `Environment.Keys.DIST` | `forgespi.locating.IModLocator` (SPI 3.2/4.0), `scanMods()` returns `List<IModFile>` |
| Forge 1.17 to 1.20.1 | yes | `Environment.Keys.DIST` | same class name, SPI 7.0, `scanMods()` returns `List<ModFileOrException>` |
| NeoForge 1.20.2+ | yes | `FMLEnvironment.dist` | `neoforgespi.locating.IModFileCandidateLocator`, `findCandidates(ILaunchContext, IDiscoveryPipeline)` |
| Quilt | opt-in | `MinecraftQuiltLoader.getEnvironmentType()` | `org.quiltmc.loader.api.plugin.QuiltLoaderPlugin`, see below |
| Fabric | **no** | `FabricLoader.getEnvironmentType()` | see below |
| Forge 1.12.2 and older | yes | `FMLLaunchHandler.side()` | `IFMLLoadingPlugin` coremod, a separate codebase rather than an adapter |

Forge keeps the same class name across generations while changing the signature, so those two are binary incompatible with each other: a single class cannot implement both. Supporting more loaders means a shared core plus one thin adapter each.

Only the adapter is loader-specific. `Syncer` imports nothing from any loader, which is what makes this cheap.

## Sides

The same jar runs on a player's machine and on a dedicated server. There is no separate server download: the adapter asks its loader which side it is, and requests the matching set.

Every mod carries one of three values, and `Both` is the default, so a server that has never classified anything behaves exactly as it did before sides existed:

| Side | Goes to players | Goes to the dedicated server |
| --- | --- | --- |
| `Both` | yes | yes |
| `Client only` | yes | no |
| `Server only` | no | yes |

That matters because a client-only mod on a dedicated server is at best pointless and at worst a crash on boot - JEI, Xaero's Minimap, Sodium and Iris all have no business there.

Mechanically it is one query parameter. A client asks for `/api/manifest`, exactly as every jar shipped before this did, and gets `Both` plus `Client only`. A dedicated server asks for `/api/manifest?side=server` and gets `Both` plus `Server only`. The blob endpoint applies the same rule, so a jar cannot be fetched by hash by a side it was not sent to, and the manifest bakes the side into the download URLs it hands out.

HOPPER fills the side in for you wherever the source knows it: the `env` object on an `.mrpack` entry, the `client-overrides/` and `server-overrides/` folders, Modrinth's `client_side` and `server_side`, and failing all of those the `environment` field in a jar's own `fabric.mod.json` or `quilt.mod.json`. A Prism or CurseForge pack carries no side, so those fall back to the jar. Anything with no signal at all stays `Both`, and the Mods page is where you correct it - select any number of rows and set them at once.

Exporting reverses the same knowledge, and the three formats differ because they are different things. An `.mrpack` records a side per file, so a side survives a round trip through it unchanged. A CurseForge pack carries no side and is a distributable a server operator installs too, so it ships both sets together. A Prism instance is one machine's game directory rather than a distributable, and in practice a client one, so it gets the jars a player gets and the export dialog names what it left out.

### Fabric

Fabric has no public pre-discovery hook. Its only lever is the `fabric.addMods` system property, read by `ArgumentModCandidateFinder` during discovery, and a system property means a JVM argument, which means a launcher setting. That is exactly what HOPPER avoids, because the CurseForge app does not expose one.

Everything else in Fabric's discovery lives under `net.fabricmc.loader.impl`, which is internal and free to break at any time.

So Fabric gets the honest version instead: sync from the `preLaunch` entrypoint, and when something changed, tell the player a restart is needed. That is what AutoModpack does, and for the same reason. Same `core/`, worse promise.

### Quilt

Quilt has the right hook, and a declaration Quilt refuses to read. `V1ModMetadataImpl` throws a `ParseException` - a hard failure, not a degradation - the moment it sees the `experimental_quilt_loader_plugin` key while `-Dloader.experimental.allow_loading_plugins=true` is unset. So a Quilt server is served `hopper-fabric.jar` by default: Quilt runs Fabric mods through `StandardFabricPlugin`, the `preLaunch` entrypoint works unchanged, and the player gets the Fabric promise - it works, it needs a restart.

The plugin jar is built, tested and shipped as `hopper-quilt-plugin.jar` all the same. A player who has set that JVM flag downloads it from `GET /api/servers/{id}/jar?variant=quilt-plugin` and gets same-launch loading. Asking for that variant on any other loader is a 400 rather than a jar that will not parse.

## Limits

- HOPPER cannot update its own jar; it is open while it runs.
- Someone places that first jar by hand. Nothing can reach a client that has not run anything yet.
- One token per server, shared by every client of that server, sitting in plain text inside a jar on machines you do not control. It gates read access to that one server's mod set. Rotating it invalidates every jar already handed out.
- `username` is self-reported and unverified. It labels a row in the dashboard; it is not an identity.
