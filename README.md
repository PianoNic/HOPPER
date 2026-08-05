<p align="center">
  <img src="assets/hopper-icon.svg" width="120" alt="HOPPER logo" />
</p>
<p align="center">
  <strong>HOPPER</strong><br/>
  One list on the server. Every client in sync, before the game starts.
</p>

---

Keeps every client's mods in sync with one server-side list. No restart, and it
works under Prism, CurseForge, and the vanilla launcher alike.

Forge 1.20.1.

## How it works

HOPPER is a **mod locator**, not a mod. Forge's `ModDirTransformerDiscoverer`
walks `mods/`, reads each jar's module descriptor, and lifts any jar that
*provides* `net.minecraftforge.forgespi.locating.IModLocator` into the SERVICE
layer — which is built **before** `ModDiscoverer` scans for mods.

So HOPPER downloads the required set, and FML then picks those jars up in the
same launch. That is why there is no restart, and why it does not care which
launcher started the game: every launcher loads `mods/`.

Downloads land in `hopper/`, never in `mods/`. That directory belongs to HOPPER
outright, so there are no open file handles to fight (the reason a mod-based
approach needs a restart on Windows) and a player's own mods are never touched.

## Install

1. `./gradlew build` → `build/libs/hopper-1.0.0.jar`
2. Drop that one jar into each client's `mods/` folder, once.
3. Launch. `config/hopper.properties` is written on first run:

```properties
enabled=true
manifestUrl=https://hopper.example.com/api/manifest
token=
```

`token` is the shared client token from the server, sent as
`Authorization: Bearer <token>` on the manifest, jar and report requests. Leave
it empty and no header is sent at all.

Ship the properties file, filled in, next to the jar to skip step 3.

## Server

The API serves the mod set; adding a mod is an upload, not a script run. Only
`manifestUrl` is configured — `/api/clients/report` is derived from it, so the
two can never drift apart.

```
GET  /api/manifest        the list below
GET  /api/blobs/{sha256}  the jar bytes
POST /api/clients/report  what this client ended up with
```

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

## Run the server

```sh
cp .env.example .env      # optional; the defaults in compose.yml run as-is
docker compose up -d --build
```

Dashboard and API on `http://localhost:58722`, a throwaway IdP on `:58538`. One
image holds both: `src/HOPPER.API/Dockerfile` builds the Angular app and copies
it into the API's `wwwroot`, so the dashboard is always same-origin with the API
and there is no CORS allowlist to configure. Postgres runs as its own `db`
service on the `hopper-db` volume; the content-addressed blobs live under
`/data`, backed by the `hopper-data` volume.

Two credentials, no overlap:

- **Admin** (`/api/mods`, `/api/clients`, the dashboard) — OIDC. Point
  `Oidc__Authority` at your own IdP; the bundled `mock-oauth2-server` exists so a
  fresh checkout can log in without standing one up first, and signs a token for
  anyone who asks.
- **Client** (`/api/manifest`, `/api/blobs/{sha256}`, `/api/clients/report`) —
  the shared token from `Hopper__ClientTokens__0`, the same value that goes into
  each client's `hopper.properties`. An empty list locks those three endpoints
  rather than opening them.

`.env.example` documents every setting.

## Develop

```sh
docker compose -f compose.dev.yml up -d          # IdP on :58539
dotnet run --project src/HOPPER.API              # API on :5170
cd src/HOPPER.Frontend && bun install && bun start   # dashboard on :4200
```

Two origins here, so this is the one setup that needs
`Cors:AllowedOrigins` — already set, along with the `Oidc:*` values and a
development client token, in `src/HOPPER.API/appsettings.Development.json`.
Nothing to configure by hand.

Regenerate the typed API client after a contract change with `bun run apigen`
(reads `http://localhost:5170/openapi/v1.json`).

### Tests

```sh
dotnet test                                      # 86 tests
cd src/HOPPER.Frontend && bun run test           # 6 tests
```

`dotnet test` needs `global.json`'s `test.runner` opt-in: TUnit is a
Microsoft.Testing.Platform framework and the .NET 10 SDK dropped the VSTest
bridge, so without it the build fails outright rather than reporting no tests.

The .NET suite covers the fixed wire format in both directions (including that a
manifest field name survives a global naming-policy change), the blob store's
hashing and traversal rejection, the filename validator against the Java
client's own rules, and the client/admin auth split over HTTP against the real
pipeline via `WebApplicationFactory`.

### Migrations

```sh
dotnet ef migrations has-pending-model-changes -p src/HOPPER.Infrastructure -s src/HOPPER.API
dotnet ef migrations add <Name> -p src/HOPPER.Infrastructure -s src/HOPPER.API
```

`dotnet ef database update` is not needed — the API migrates at boot, so an
upgrade is a restart.

## Behaviour

- A file is downloaded only if it is missing or its sha256 does not match.
- Anything in `hopper/` that the manifest no longer lists is deleted, except
  `hopper/client-id`.
- After a successful sync the client POSTs `{clientId, username, mods:[{file,
  sha256}]}` so the dashboard can show who is running what. `clientId` is a UUID
  generated once and kept in `hopper/client-id`; `username` comes from the launch
  arguments and is `null` on a server or a launcher that does not pass one.
- **A failed report never fails the sync or the launch**, and never delays it
  beyond its 10s timeout. Nothing downstream depends on it.
- Filenames from the manifest are rejected if they contain a path separator,
  `..`, a leading dot, or do not end in `.jar`.
- A download whose hash does not match is discarded, not installed.
- **A failed sync never blocks the launch.** Offline, server down, malformed
  manifest — the game starts with the last set that downloaded successfully,
  and the failure is logged and shown on the loading screen.

## Limits

- HOPPER cannot update its own jar; it is open while it runs. It rarely changes.
- Someone has to place that first jar by hand. Unavoidable in any design —
  nothing can reach a client that has not run anything yet.
- Forge 1.20.1 only. The locator API changed across 1.16 / 1.20.1 / NeoForge,
  so other generations need their own build. `Syncer` carries over unchanged;
  only `HopperLocator` is version-specific.
- One shared token for every client, sitting in plain text in a properties file
  on machines you do not control. It gates read access to the mod set and
  nothing else, and rotating it means redistributing the file.
- `username` is self-reported and unverified. It labels a row in the dashboard;
  it is not an identity.

## Build

No ForgeGradle and no reobf — a locator never touches a Minecraft class, so
there is nothing to remap. Dependencies are pinned to what
`fmlloader-1.20.1-47.1.3.pom` itself declares: forgespi 7.0.1,
modlauncher 10.0.9.

```sh
cd locator && ./gradlew build   # runs the tests too
```
