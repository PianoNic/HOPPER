<p align="center">
  <img src="assets/hopper-icon.svg" width="140" alt="HOPPER Logo" />
</p>
<p align="center">
  <strong>HOPPER</strong><br/>
  One list on the server. Every client in sync, before the game starts.
</p>
<p align="center">
  <a href="https://github.com/PianoNic/HOPPER"><img src="https://badgetrack.pianonic.ch/badge?tag=hopper&label=visits&color=0B1220&style=flat" alt="visits" /></a>
  <a href="#%EF%B8%8F-run-the-server"><img src="https://img.shields.io/badge/Self--Host-Instructions-0B1220.svg" alt="Self-hosting" /></a>
  <img src="https://img.shields.io/badge/Forge-1.20.1-0B1220.svg" alt="Forge 1.20.1" />
  <img src="https://img.shields.io/badge/.NET-10-0B1220.svg" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Angular-21-0B1220.svg" alt="Angular 21" />
</p>

---

> **⚠️ Early development.** Expect rough edges and breaking changes between versions.

## 🚀 What is HOPPER?

HOPPER keeps every player's mods in sync with one list you control on the server.
Your friends install a single jar once. From then on, adding a mod is an upload -
no zip files in Discord, no "did you update yet?", **and no restart.**

It works under Prism, CurseForge and the vanilla launcher alike, because the one
thing every launcher has in common is that it loads `mods/`.

## ✨ Features

- **No restart.** Mods land in the same launch they were downloaded in. Most syncers
  need a second start before the game sees new files; HOPPER does not.
- **Launcher-agnostic.** One jar in `mods/` works everywhere - no pre-launch command,
  no custom JVM arguments, no launcher-specific setup.
- **Zero client configuration.** The jar you download already carries its server's URL
  and token, so the player configures nothing.
- **Multiple servers.** Each server has its own mod list, its own token and its own
  generated jar. A client only ever sees the server it belongs to.
- **Pack import.** Point it at a Modrinth `.mrpack` or a CurseForge export - by file or
  by URL - the way Prism Launcher does it.
- **Verified downloads.** Every jar is checked against its SHA-256 before it is
  installed. A mismatch is discarded, not loaded.
- **It never blocks the launch.** Server down, offline, bad manifest - the game starts
  with the last set that downloaded successfully, and says so on the loading screen.

## 🧩 How it works

HOPPER is a **mod locator**, not a mod. Forge's `ModDirTransformerDiscoverer` walks
`mods/`, reads each jar's module descriptor, and lifts any jar that *provides*
`net.minecraftforge.forgespi.locating.IModLocator` into the SERVICE layer - which is
built **before** `ModDiscoverer` scans for mods.

So HOPPER downloads the required set, and FML then picks those jars up in the same
launch. That is the whole trick, and why there is no restart.

Downloads land in `hopper/`, never in `mods/`. That directory belongs to HOPPER
outright, so there are no open file handles to fight - the reason a mod-based approach
needs a restart on Windows - and a player's own mods are never touched.

## 📦 Install a client

1. Open a server in the dashboard and hit **Download client jar**.
2. Drop that one jar into the player's `mods/` folder.
3. Launch.

There is no step 4. The jar is a copy of the template with one extra entry written into
it, `/hopper-server.properties`, holding that server's id, manifest URL and token. A
second server is a second download, not a file to edit.

Settings merge **per key**, jar first:

| key | jar | `config/hopper.properties` |
| --- | --- | --- |
| `serverId` | ✅ written on download | fallback |
| `manifestUrl` | ✅ written on download | fallback |
| `token` | ✅ written on download | fallback |
| `enabled` | never written | **the only source** |

`enabled` is deliberately left out of the jar, so `enabled=false` still stops a
downloaded jar from syncing - the player keeps a local kill switch on a jar that
otherwise configures itself. A value that is present but blank counts as unset and
falls through, so an unpatched template behaves exactly like a jar with no embedded file.

## 🔌 API

The API serves the mod set; adding a mod is an upload, not a script run. The client is
given exactly one URL - its manifest - and derives `/api/clients/report` from it, so the
two can never drift apart.

```
GET  /api/manifest              the list below, for the token's server
GET  /api/blobs/{sha256}        the jar bytes
POST /api/clients/report        what this client ended up with
GET  /api/servers/{id}/jar      a client jar with all of the above baked in
```

None of the three client endpoints carries a server segment, and that is deliberate:
the shipped Java client derives its report URL from its manifest URL, so a segment on
one would silently move the other. The server comes from the token instead.

```json
{
  "mods": [
    {
      "file": "jei-1.20.1-15.2.0.27.jar",
      "url": "https://hopper.example.com/api/blobs/…",
      "sha256": "…",
      "size": 1234567
    }
  ]
}
```

## 🖥️ Run the server

```sh
cp .env.example .env      # optional; the defaults in compose.yml run as-is
docker compose up -d --build
```

Dashboard and API on `http://localhost:58722`, a throwaway IdP on `:58538`. One image
holds both - the Dockerfile builds the Angular app into the API's `wwwroot`, so the
dashboard is always same-origin and there is no CORS allowlist to configure. Postgres
runs as its own `db` service; the content-addressed blobs live under `/data`.

Two credentials, no overlap:

- **Admin** (dashboard, `/api/servers/...`) - OIDC. Point `Oidc__Authority` at your own
  IdP; the bundled `mock-oauth2-server` exists so a fresh checkout can log in without
  standing one up first.
- **Client** (`/api/manifest`, `/api/blobs/{sha256}`, `/api/clients/report`) - a
  per-server token, minted by HOPPER and resolved to a server on every request. A token
  matching no server is a 401, and a database with no servers rejects every client
  rather than opening the door.

## ⚙️ Configuration

| Variable | Default | Description |
| --- | --- | --- |
| `ConnectionStrings__HopperDatabase` | compose `db` service | Postgres connection string. HOPPER is Postgres-only. |
| `Blobs__Directory` | `/data/blobs` | Content-addressed jar store. Shared across servers, so a jar used twice is stored once. |
| `Hopper__PublicBaseUrl` | derived from the request | Host written into every manifest URL. Leave unset behind a proxy that sends `X-Forwarded-*`. |
| `Hopper__BootstrapClientToken` | `change-me` | Token of the `Default` server created on an empty database. Applied only while no servers exist. |
| `Hopper__LocatorTemplatePath` | built into the image | The template jar that `GET /api/servers/{id}/jar` patches per server. |
| `Oidc__Authority` | bundled mock IdP | Your OIDC issuer for admin access. |
| `CurseForge__ApiKey` | *(unset)* | Optional. Without it, CurseForge imports list unresolvable mods for manual upload - the same thing Prism's blocked-mods dialog does. |

`.env.example` documents every setting.

## 🛠️ Develop

```sh
docker compose -f compose.dev.yml up -d             # Postgres + IdP
dotnet run --project src/HOPPER.API                 # API on :5170
cd src/HOPPER.Frontend && bun install && bun start  # dashboard on :4200
```

Two origins here, so this is the one setup that needs `Cors:AllowedOrigins` - already
set, along with the `Oidc:*` values, in `src/HOPPER.API/appsettings.Development.json`.

Regenerate the typed API client after a contract change with `bun run apigen`.

### Tests

```sh
dotnet test                                      # needs Docker (Testcontainers)
cd src/HOPPER.Frontend && bun run test
```

The .NET suite runs against a throwaway Postgres container rather than a different
engine than production uses. It covers the fixed wire format in both directions,
cross-server isolation, blob sharing and orphan collection, the blob store's hashing
and traversal rejection, and the client/admin auth split over HTTP against the real
pipeline.

### Migrations

```sh
dotnet ef migrations has-pending-model-changes -p src/HOPPER.Infrastructure -s src/HOPPER.API
dotnet ef migrations add <Name> -p src/HOPPER.Infrastructure -s src/HOPPER.API
```

`dotnet ef database update` is not needed - the API migrates at boot, so an upgrade is
a restart.

## 🔨 Build the locator

No ForgeGradle and no reobf - a locator never touches a Minecraft class, so there is
nothing to remap. Dependencies are pinned to what `fmlloader-1.20.1-47.1.3.pom` itself
declares: forgespi 7.0.1, modlauncher 10.0.9.

```sh
cd locator && ./gradlew build
```

→ `locator/build/libs/hopper-1.0.0.jar`. That is the **template**: point
`Hopper:LocatorTemplatePath` at it and `GET /api/servers/{id}/jar` serves per-server
copies. The Dockerfile builds it in a JDK stage, so a deployed HOPPER has one without
anyone running Gradle.

The wrapper is checked in and pins both the Gradle version and its SHA-256. Compilation
uses `--release 17` rather than a toolchain: the bytecode and visible API are Java 17
either way, but the build runs on whatever JDK is installed instead of demanding one
exact version. Warnings are errors (`-Xlint:all -Werror`) - a mistake here is a launch
that silently does nothing.

The template must contain `META-INF/services/net.minecraftforge.forgespi.locating.IModLocator`
(that one file is what makes Forge lift it into the SERVICE layer) and
`Automatic-Module-Name: hopper` in its manifest. `LocatorJarBuilder` refuses to patch a
jar missing the services file rather than serve one that installs cleanly and then does
nothing.

## 🎛️ Behaviour

- A file is downloaded only if it is missing or its sha256 does not match.
- Anything in `hopper/` that the manifest no longer lists is deleted, except
  `hopper/client-id`.
- After a successful sync the client POSTs `{clientId, username, mods:[{file, sha256}]}`
  so the dashboard can show who is running what. `clientId` is a UUID generated once and
  kept in `hopper/client-id`; `username` comes from the launch arguments and is `null` on
  a server or a launcher that does not pass one. No `serverId` in that body, on purpose -
  the server is decided by the bearer token, so the shape is byte-identical to the
  single-server client's.
- **A failed report never fails the sync or the launch**, and never delays it beyond its
  10s timeout.
- Filenames from the manifest are rejected if they contain a path separator, `..`, a
  leading dot, or do not end in `.jar`.
- A download whose hash does not match is discarded, not installed.
- **A failed sync never blocks the launch.** Offline, server down, malformed manifest -
  the game starts with the last set that downloaded successfully, and the failure is
  logged and shown on the loading screen.

## 📋 Limits

- **Forge 1.20.1 only.** The locator SPI changed across 1.16 / 1.20.1 / NeoForge, so
  other generations need their own build. `Syncer` carries over unchanged; only
  `HopperLocator` is version-specific.
- HOPPER cannot update its own jar - it is open while it runs. It rarely changes.
- Someone places that first jar by hand. Unavoidable in any design: nothing can reach a
  client that has not run anything yet.
- One token per server, shared by every client of that server and sitting in plain text
  inside a jar on machines you do not control. It gates read access to that one server's
  mod set and nothing else. Rotating it invalidates every jar already handed out for that
  server; the dashboard says so before it does it, and the fix is a fresh download.
- `username` is self-reported and unverified. It labels a row in the dashboard; it is not
  an identity.

## 📄 License

TBD.

---

<p align="center">Made with care by <a href="https://github.com/PianoNic">PianoNic</a></p>
