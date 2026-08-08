# Self-hosting HOPPER

One image holds both halves: the Dockerfile builds the Angular dashboard into the API's `wwwroot`, so the dashboard is always same-origin with the API and there is no CORS allowlist to configure. It also builds the locator template jars, one per loader generation, in a JDK stage, which is what `GET /api/servers/{id}/jar` patches per server.

## Run it

**1. Create a `compose.yml`:**

```yaml
services:
  db:
    image: postgres:18.3
    container_name: hopper-postgres
    environment:
      POSTGRES_DB: hopper
      POSTGRES_USER: hopper
      POSTGRES_PASSWORD: hopper
    healthcheck:
      # depends_on alone only waits for the process, not for it to accept connections.
      test: ["CMD-SHELL", "pg_isready -U hopper -d hopper"]
      interval: 5s
      retries: 10
    volumes:
      # /var/lib/postgresql, not /var/lib/postgresql/data: the 18+ images store the cluster in a
      # major-version subdirectory and refuse to start when the old path is mounted directly.
      - hopper-db:/var/lib/postgresql
    restart: unless-stopped

  hopper:
    # Also published to Docker Hub as docker.io/pianonic/hopper with the same tags, if you prefer
    # that registry. Both are built from the same release and carry linux/amd64 and linux/arm64.
    image: ghcr.io/pianonic/hopper:latest
    container_name: hopper
    ports:
      - "58722:8080"
    environment:
      ConnectionStrings__HopperDatabase: "Host=db;Port=5432;Database=hopper;Username=hopper;Password=hopper"
      Oidc__Authority: "https://id.example.com/realms/hopper"
      Oidc__ClientId: hopper
      Oidc__RedirectUri: "https://hopper.example.com/"
      Oidc__PostLogoutRedirectUri: "https://hopper.example.com/"
    env_file:
      - path: .env
        required: false
    depends_on:
      db:
        condition: service_healthy
    volumes:
      - hopper-data:/data
    restart: unless-stopped

volumes:
  hopper-data:
  hopper-db:
```

**2. Start it:**

```bash
docker compose up -d
```

The dashboard is live at `http://localhost:58722`.

## Try it without an IdP

`compose.demo.yml` adds a throwaway OIDC provider, so a fresh checkout can log in without standing up Keycloak first. It signs a token for anyone who asks, which is fine on your own machine and nowhere else - which is why it is a separate file that a plain `docker compose up -d` cannot pick up by accident.

```bash
git clone https://github.com/PianoNic/HOPPER.git
cd HOPPER
docker compose -f compose.yml -f compose.demo.yml up -d --build
```

Dashboard on `:58722`, the mock IdP on `:58538`. A server named `Default` exists immediately, so you can download its jar without configuring anything. Its client token is generated at first boot and shown on the server's setup page - nothing in this repository knows it.

## `.env` overrides everything

Every value in the repo's `compose.yml` is written `${VAR:-default}`, so anything you put in `.env` wins. That shape matters: Compose ranks an inline `environment:` value above `env_file`, so a literal there would beat your `.env` without saying so. Keep the interpolation when you add a setting.

`compose.yml` on its own is a real deployment. It starts no identity provider and defaults `Oidc__Authority` to empty, so admin sign-in fails until you name yours - which beats quietly trusting one that signs tokens for anybody.

## Grant yourself the admin role

HOPPER requires the `hopper-admin` role on the admin surface, so pointing it at an existing realm does not hand your whole user base the keys. Set it up once:

1. Create a realm role named `hopper-admin` and assign it to your own account.
2. Make sure it reaches the token. In Keycloak that means a mapper on the `hopper` client putting realm roles into a claim named `roles` - the built-in "realm roles" mapper with Token Claim Name set to `roles`.
3. Sign in. A token without the role gets a 403, not a 401, so a rejection here is a role problem rather than a login problem.

Rename the role with `Oidc__AdminRole`, or set it empty to accept any authenticated user - reasonable only when HOPPER is the realm's only client.

## Installing on a dedicated server

The dedicated server takes the **same jar** the players do. There is no separate server download: the adapter asks its loader whether it is a client or a server and requests the matching mod set. Drop `<slug>-hopper.jar` into the server's `mods/` folder next to the loader's own files, exactly as on a client, and start it.

What you should see in the server log, before the world loads:

```
[HOPPER] syncing from https://hopper.example.com/api/manifest (server <id>)
[HOPPER] downloading appleskin-forge-mc1.20.1-2.5.1.jar (46 KiB)
[HOPPER] 2 mod(s) ready
```

It uses the same per-server token as the players, so nothing extra needs configuring. On the Clients page it appears as **Dedicated server** rather than as a nameless player - a server has no username to report, so the side is its identity.

**Set the side on anything that only belongs on one of them.** A client-only mod on a dedicated server is at best pointless and at worst a crash on boot, which is what the Side column on the Mods page exists for. HOPPER fills it in automatically when the pack or Modrinth says, so in practice you are correcting a handful rather than classifying everything. See [how it works](how-it-works.md#sides).

Forge, NeoForge and Fabric dedicated servers are all supported. The Fabric caveat about restarts applies to clients only - a dedicated server has no in-launch discovery hook to miss, because it is restarted deliberately.

## Two credentials, no overlap

- **Admin** (the dashboard, `/api/servers/...`) is OIDC. Point `Oidc__Authority` at your own provider, and grant yourself `hopper-admin`.
- **Client** (`/api/manifest`, `/api/blobs/{sha256}`, `/api/clients/report`) is a per-server token, minted by HOPPER and resolved to a server on every request. A token matching no server is a 401, and a database with no servers rejects every client rather than opening the door.

A fresh deployment has no servers and no tokens. Create the first one in the dashboard; it is minted a token there, and the jar you download already carries it.

## Configuration

| Variable | Default | Description |
| --- | --- | --- |
| `ConnectionStrings__HopperDatabase` | compose `db` service | Postgres connection string. HOPPER is Postgres-only. |
| `Blobs__Directory` | `/data/blobs` | Content-addressed jar store. Shared across servers, so a jar used twice is stored once. Staged packs and export scratch live on the same volume, and HOPPER reclaims unreferenced files once they are past the grace period below. |
| `Hopper__PublicBaseUrl` | derived from the request | Host written into every manifest URL. Leave unset behind a proxy that sends `X-Forwarded-*`. |
| `Hopper__TrustedProxies` | loopback and the private ranges | Peers whose `X-Forwarded-For`, `-Proto` and `-Host` are believed, as IP addresses or CIDR networks, comma-separated. Setting it **replaces** the built-in list, so name every proxy that fronts HOPPER. Blank counts as unset and keeps the built-in list; write `127.0.0.1,::1` to believe nothing but the container itself. |
| `Hopper__LocatorTemplateDirectory` | built into the image | Directory of template jars, one per loader generation. The download endpoint picks by the server's loader and Minecraft version. |
| `Hopper__PackDownloadHosts` | Modrinth, GitHub, GitLab and the two forgecdn hosts | Hosts a pack may be downloaded from. Setting it **replaces** the built-in list rather than adding to it, so list every host you want, including `cdn.modrinth.com` and `edge.forgecdn.net`. |
| `Hopper__MaxImportBytes` | `2147483648` | Ceiling on one import: the compressed pack, the archive an admin uploads, and the summed size the pack declares. |
| `Hopper__MaxModBytes` | `536870912` | Ceiling on one jar, decompressed or downloaded. A single file over it fails on its own; the rest of the batch still lands. |
| `Hopper__MaxPackMetadataBytes` | `8388608` | Ceiling on a pack's own index files. |
| `Hopper__MaxReportedMods` | `2000` | Most mods one client may report in a single call. A large pack is roughly 400. |
| `Hopper__BlobReclaimInterval` | `01:00:00` | How often the reclaim sweep runs after the one at startup. Zero leaves only the startup sweep. |
| `Hopper__BlobReclaimGrace` | `01:00:00` | How long an unreferenced blob, a stale upload part and export scratch survive before the sweep takes them. |

The sweep refuses to run at all when the database holds no servers but the blob store is not empty. That combination is what a restored, fresh or misconfigured database looks like from the inside, and sweeping then would delete every jar - so HOPPER logs a warning and leaves the store alone. Point it at the right database, or empty the store by hand.
| `Hopper__ImportStallTimeout` | `02:00:00` | How long an import may sit queued or running without progress before HOPPER ends it and frees its staged pack. |
| `Oidc__Authority` | *(unset)* | OIDC issuer for admin access. Nothing is trusted until you set it. |
| `Oidc__AdminRole` | `hopper-admin` | Role an account must carry to administer HOPPER. Empty means any authenticated user. |
| `Oidc__ValidAudiences__0` | `Oidc__ClientId` | Accepted `aud` values. Set when your issuer stamps something else. |
| `Oidc__InternalAuthority` | unset | Set when the API reaches the IdP on a different address than the browser does. |
| `Otel__Endpoint` | *(unset)* | OTLP collector, for example `http://collector:4317`. Unset registers no exporter at all rather than retrying against a collector that is not there. |
| `Otel__ServiceName` | `hopper` | Service name on the exported traces and metrics. |
| `CurseForge__ApiKey` | *(unset)* | Optional. Without it, CurseForge imports list unresolvable mods for manual upload. |

HOPPER runs as a single API instance. The import queue lives in that process, so a queued or running import
does not survive a restart. HOPPER ends those imports at startup and frees their staged packs. Running two API
replicas against one database would have each of them end the other's live imports.

`.env.example` documents every setting.

## Health checks

The image carries a `HEALTHCHECK`, so `docker ps` reports the container's real state and other services can wait on `condition: service_healthy`. Two endpoints, both anonymous:

| Path | Answers |
| --- | --- |
| `/health/live` | 200 as long as the process is up. Runs no check, on purpose: restarting the container because Postgres is down turns one outage into a crash loop. |
| `/health/ready` | 200 only when the database answers, the blob directory is writable and the locator templates are present. This is the one to point a load balancer at. |

Docker does not restart an unhealthy container by itself, so this reports rather than repairs. Wire it to whatever does the restarting in your setup.

## Behind a reverse proxy

Leave `Hopper__PublicBaseUrl` unset if your proxy sends `X-Forwarded-Proto` and `X-Forwarded-Host`; the manifest derives the download host from the request. Set it explicitly when the proxy does not forward them, or when clients dial a different name than the API sees. Getting this wrong means clients receive manifest URLs pointing at the wrong host.

`X-Forwarded-*` is only believed from a peer in `Hopper__TrustedProxies`, and only the last entry of the chain - the one your proxy appended itself. The shipped default is loopback plus the private ranges, so a proxy on the same Docker network or on the host works with nothing configured, while a request arriving from a public address cannot claim to come from somewhere else. Narrow it to your proxy's own address if HOPPER shares its network with anything you would rather not have speaking for a client:

```
Hopper__TrustedProxies=172.18.0.7,10.0.0.0/8
```

Narrow it too far and none of the three headers is believed, not just `X-Forwarded-For`: the manifest then derives its URLs from the address the container sees, and clients get links to that instead of to your host name. Either widen the list or set `Hopper__PublicBaseUrl`. Too wide, and the client IP on the Clients page - the one thing HOPPER derives rather than accepts - becomes as forgeable as everything else on that page.

## Where the image comes from

Every release is pushed to both registries, from the same build, for `linux/amd64` and `linux/arm64`:

```
ghcr.io/pianonic/hopper
docker.io/pianonic/hopper
```

Both carry the same tags: the full version (`0.1.0`), the minor (`0.1`), the major (`0`), and `latest`. `latest` is skipped for a prerelease, so a preview never becomes the default pull.

Pin the full version in production. `latest` moves under you on the next release, and an upgrade is meant to be a decision rather than a surprise.

## Upgrading

The API migrates the database at boot, so an upgrade is a `docker compose pull` and a restart. There is no separate migration step.
