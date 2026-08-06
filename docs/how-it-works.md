# How HOPPER works

## No restart

HOPPER is a **mod locator**, not a mod. Forge's `ModDirTransformerDiscoverer` walks `mods/`, reads each jar's module descriptor, and lifts any jar that *provides* `net.minecraftforge.forgespi.locating.IModLocator` into the SERVICE layer. That layer is built **before** `ModDiscoverer` scans for mods.

So HOPPER downloads the required set, and FML then picks those jars up in the same launch. That is why there is no restart, and why it does not care which launcher started the game.

A normal `@Mod` runs after the jar scan. On Windows the open jars cannot be replaced, which forces a restart plus a second process to apply the files. HOPPER avoids that entirely by never writing into `mods/`: downloads land in `hopper/`, a directory it owns outright, so a player's own mods are never touched either.

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
GET  /api/servers/{id}/jar      a client jar with all of the above baked in
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
- Anything in `hopper/` the manifest no longer lists is deleted, except `hopper/client-id`.
- A download whose hash does not match is discarded, not installed.
- Filenames are rejected if they contain a path separator, `..`, a leading dot, or do not end in `.jar`.
- A failed report never fails the sync, and a failed sync never blocks the launch.

## Version coverage

The locator SPI changed across Forge generations, so one jar cannot serve all of them:

| Generation | Interface | `scanMods()` returns |
| --- | --- | --- |
| Forge 1.16.x | `forgespi.locating.IModLocator` (SPI 3.2/4.0) | `List<IModFile>` |
| Forge 1.17 to 1.20.1 | same name, SPI 7.0 | `List<ModFileOrException>` |
| NeoForge 1.20.2+ | `neoforgespi.locating.IModFileCandidateLocator` | different package |

Forge keeps the same class name across generations but the signatures are binary incompatible, so a single jar cannot implement both. Supporting more versions means a shared core plus one thin adapter per generation.

Fabric and Quilt have no public SPI that runs before mod discovery, which is why AutoModpack restarts the game there.

## Limits

- HOPPER cannot update its own jar; it is open while it runs.
- Someone places that first jar by hand. Nothing can reach a client that has not run anything yet.
- One token per server, shared by every client of that server, sitting in plain text inside a jar on machines you do not control. It gates read access to that one server's mod set. Rotating it invalidates every jar already handed out.
- `username` is self-reported and unverified. It labels a row in the dashboard; it is not an identity.
