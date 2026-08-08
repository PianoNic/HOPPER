# Self-hosting HOPPER

HOPPER ships as one image. It holds the API, the dashboard and the locator template jars.

The dashboard is built into the API's `wwwroot`, so it is always same-origin and there is no CORS
list to configure. The template jars are built in a JDK stage, one per loader generation, and
`GET /api/servers/{id}/jar` patches a per-server copy out of them.

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
      test: ["CMD-SHELL", "pg_isready -U hopper -d hopper"]
      interval: 5s
      retries: 10
    volumes:
      - hopper-db:/var/lib/postgresql
    restart: unless-stopped

  hopper:
    # Also on docker.io/pianonic/hopper, same tags, same build.
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

::: warning Postgres 18 mounts at `/var/lib/postgresql`
Not `/var/lib/postgresql/data`. The 18+ images keep the cluster in a major-version subdirectory and
restart-loop when the old path is mounted directly, with nothing ever listening.
:::

The healthcheck is not decoration. `depends_on` alone waits for the process, not for it to accept
connections.

## Try it without an IdP

`compose.demo.yml` adds a throwaway OIDC provider, so a fresh checkout can sign in without standing
up Keycloak first.

```bash
git clone https://github.com/PianoNic/HOPPER.git
cd HOPPER
docker compose -f compose.yml -f compose.demo.yml up -d --build
```

Dashboard on `:58722`, mock IdP on `:58538`. A server named `Default` exists immediately, so you can
download its jar without configuring anything. Its client token is generated at first boot and shown
on the server's setup page.

::: danger Never deploy the demo file
The mock provider signs a token for anyone who asks. It is a separate file precisely so a plain
`docker compose up -d` cannot pick it up by accident.
:::

## `.env` overrides everything

Every value in the repo's `compose.yml` is written `${VAR:-default}`, so anything in `.env` wins.

::: warning Keep the interpolation
Compose ranks an inline `environment:` value above `env_file`. A literal there beats your `.env`
without saying so.
:::

`compose.yml` on its own is a real deployment. It starts no identity provider and leaves
`Oidc__Authority` empty, so admin sign-in fails until you name yours.

## Grant yourself the admin role

HOPPER requires the `hopper-admin` role on the admin surface. Pointing it at an existing realm does
not hand your whole user base the keys. Set it up once:

1. Create a realm role named `hopper-admin` and assign it to your own account.
2. Make sure it reaches the token. In Keycloak that means a mapper on the `hopper` client putting realm roles into a claim named `roles` - the built-in "realm roles" mapper with Token Claim Name set to `roles`.
3. Sign in. A token without the role gets a 403, not a 401. A rejection here is a role problem, not
   a login problem.

Rename the role with `Oidc__AdminRole`. Setting it empty accepts any authenticated user, which is
reasonable only when HOPPER is the realm's only client.

## Installing on a dedicated server

The dedicated server takes the **same jar** the players do. There is no separate server download.
The adapter asks its loader which side it is on and requests the matching mod set.

Drop `<slug>-hopper.jar` into the server's `mods/` folder, exactly as on a client, and start it.

What you should see in the server log, before the world loads:

```
[HOPPER] syncing from https://hopper.example.com/api/manifest (server <id>)
[HOPPER] downloading appleskin-forge-mc1.20.1-2.5.1.jar (46 KiB)
[HOPPER] 2 mod(s) ready
```

It uses the same per-server token as the players. On the Clients page it appears as **Dedicated
server**, because a server has no username to report.

Forge, NeoForge and Fabric dedicated servers are all supported. The Fabric restart caveat applies to
clients only.

::: warning Set the side on anything one-sided
A client-only mod on a dedicated server is at best pointless and at worst a crash on boot. That is
what the Side column on the Mods page is for. HOPPER fills it in when the pack or Modrinth says, so
you are correcting a handful rather than classifying everything. See
[how it works](how-it-works.md#sides).
:::

## Two credentials, no overlap

- **Admin** (the dashboard, `/api/servers/...`) is OIDC. Point `Oidc__Authority` at your own provider, and grant yourself `hopper-admin`.
- **Client** (`/api/manifest`, `/api/blobs/{sha256}`, `/api/clients/report`) is a per-server token,
  minted by HOPPER and resolved to a server on every request. A token matching no server is a 401.

A fresh deployment has no servers and no tokens. Create the first one in the dashboard. It is minted
a token there, and the jar you download already carries it.

## Configuration

`.env.example` documents every setting.

### Storage

| Variable | Description | Default |
| --- | --- | --- |
| `ConnectionStrings__HopperDatabase` | Postgres connection string. HOPPER is Postgres-only. | compose `db` |
| `Blobs__Directory` | Content-addressed jar store. A jar used twice is stored once. | `/data/blobs` |
| `DataProtection__Directory` | Where the ASP.NET key ring is written. | `keys` beside the blobs |
| `Hopper__BlobReclaimInterval` | How often the reclaim sweep runs after the one at startup. Zero leaves only the startup sweep. | `01:00:00` |
| `Hopper__BlobReclaimGrace` | How long an unreferenced blob survives before the sweep takes it. | `01:00:00` |

::: warning The sweep can refuse to run
No servers in the database plus a non-empty blob store is what a restored or misconfigured database
looks like from the inside. Sweeping then would delete every jar. HOPPER logs a warning and leaves
the store alone instead. Point it at the right database, or empty the store by hand.
:::

### Networking

| Variable | Description | Default |
| --- | --- | --- |
| `Hopper__PublicBaseUrl` | Host written into every manifest URL. Leave unset behind a proxy that sends `X-Forwarded-*`. | from the request |
| `Hopper__TrustedProxies` | Peers whose `X-Forwarded-*` headers are believed, as comma-separated IPs or CIDRs. Setting it replaces the built-in list. | loopback and private ranges |
| `Hopper__PackDownloadHosts` | Hosts a pack may be downloaded from. Setting it replaces the built-in list. | Modrinth, GitHub, GitLab, forgecdn |

### Authentication

| Variable | Description | Default |
| --- | --- | --- |
| `Oidc__Authority` | OIDC issuer for admin access. Nothing is trusted until you set it. | *(unset)* |
| `Oidc__AdminRole` | Role an account must carry to administer HOPPER. Empty accepts any authenticated user. | `hopper-admin` |
| `Oidc__RoleClaim` | Claim HOPPER reads membership from. | `roles` |
| `Oidc__Scope` | Scopes requested at sign-in. | `openid profile email` |
| `Oidc__ValidAudiences__0` | Accepted `aud` values. Set when your issuer stamps something else. | `Oidc__ClientId` |
| `Oidc__FetchClaimsFromUserInfo` | Read roles from the userinfo endpoint when the token carries none. | `true` |
| `Oidc__InternalAuthority` | Set when the API reaches the IdP on a different address than the browser does. | *(unset)* |

::: warning Getting the role claim wrong answers 403, not 401
Pocket ID, Authentik and Keycloak publish groups as `groups`, not `roles`. Point `Oidc__RoleClaim`
at the right one and set `Oidc__AdminRole` to a group you already have.

`Oidc__Scope` has to ask for it too. Pocket ID only emits the claim when `groups` is in the
requested scope, so use `openid profile email groups`. A claim nobody asked for is not in the token,
and that refusal looks identical to a wrong claim name.
:::

Pocket ID, Okta and Entra keep group membership out of the access token entirely. That is what
`Oidc__FetchClaimsFromUserInfo` is for: HOPPER reads the userinfo endpoint once per token and merges
what it returns. An issuer that already puts roles in the token never triggers it, and a userinfo
endpoint that fails is logged and ignored rather than failing the request.

### Limits

| Variable | Description | Default |
| --- | --- | --- |
| `Hopper__MaxImportBytes` | Ceiling on one import, compressed and declared. | `2147483648` |
| `Hopper__MaxModBytes` | Ceiling on one jar. A file over it fails alone; the rest of the batch lands. | `536870912` |
| `Hopper__MaxPackMetadataBytes` | Ceiling on a pack's own index files. | `8388608` |
| `Hopper__MaxReportedMods` | Most mods one client may report in a call. A large pack is roughly 400. | `2000` |
| `Hopper__ImportStallTimeout` | How long an import may sit without progress before HOPPER ends it. | `02:00:00` |

### Other

| Variable | Description | Default |
| --- | --- | --- |
| `Hopper__LocatorTemplateDirectory` | Directory of template jars, one per loader generation. | built into the image |
| `Otel__Endpoint` | OTLP collector, for example `http://collector:4317`. Unset registers no exporter. | *(unset)* |
| `Otel__ServiceName` | Service name on exported traces and metrics. | `hopper` |
| `CurseForge__ApiKey` | Without it, CurseForge imports list unresolvable mods for manual upload. | *(unset)* |

::: info Run one instance
The import queue lives in the API process. A queued import does not survive a restart, and HOPPER
ends those at startup to free their staged packs. Two replicas against one database would each end
the other's live imports.
:::

## One warning you will still see

```
warn: Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager[35]
      No XML encryptor configured. Key {...} may be persisted to storage in unencrypted form.
```

Expected, and left alone on purpose. Nothing is encrypted with these keys: HOPPER authenticates with
bearer tokens it does not issue, and holds no session or antiforgery state. They also sit on the
same volume as every mod jar, so anyone who can read them can already read the jars.

## Health checks

The image carries a `HEALTHCHECK`, so `docker ps` reports the real state and other services can wait
on `condition: service_healthy`. Two endpoints, both anonymous:

| Path | Answers |
| --- | --- |
| `/health/live` | 200 while the process is up. Runs no check, on purpose. |
| `/health/ready` | 200 when the database answers, the blob directory is writable and the templates are present. |

Point a load balancer at `/health/ready`. `/health/live` checks nothing deliberately: restarting the
container because Postgres is down turns one outage into a crash loop.

::: info Docker reports, it does not repair
Docker does not restart an unhealthy container by itself. Wire the healthcheck to whatever does the
restarting in your setup.
:::

## Behind a reverse proxy

Leave `Hopper__PublicBaseUrl` unset if your proxy sends `X-Forwarded-Proto` and `X-Forwarded-Host`.
The manifest derives the download host from the request. Set it explicitly when the proxy does not
forward them, or when clients dial a different name than the API sees.

`X-Forwarded-*` is believed only from a peer in `Hopper__TrustedProxies`, and only the last entry in
the chain. The default is loopback plus the private ranges, so a proxy on the same Docker network
works with nothing configured. Narrow it to your proxy's own address if HOPPER shares its network
with anything you would rather not have speaking for a client:

```
Hopper__TrustedProxies=172.18.0.7,10.0.0.0/8
```

::: warning Narrow it too far and manifest URLs break
None of the three headers is believed, not just `X-Forwarded-For`. The manifest then derives its
URLs from the address the container sees, and clients get links to that. Widen the list, or set
`Hopper__PublicBaseUrl`.

Too wide, and the client IP on the Clients page becomes as forgeable as everything else on it.
:::

## Where the image comes from

Every release is pushed to both registries, from the same build, for `linux/amd64` and
`linux/arm64`:

```
ghcr.io/pianonic/hopper
docker.io/pianonic/hopper
```

Both carry the same tags: the full version (`1.2.3`), the minor (`1.2`), the major (`1`), and
`latest`. A prerelease is not tagged `latest`, so a preview never becomes the default pull.

Pin the full version in production. `latest` moves under you on the next release.

## Backing it up

Two things hold a HOPPER install, and they only mean something together:

| What | Where | Holds |
| --- | --- | --- |
| The rows | `hopper-db` | Which mod, which side, which server, which client, and the sha256 pointing at the bytes |
| The bytes | `hopper-data` | `blobs/`, addressed by that sha256, plus the data protection keys |

Back up **both, from the same moment**. Restoring one without the other fails in one of two ways,
and neither announces itself as a backup problem:

- **Rows without bytes.** Every mod shows **Bytes missing** and every client takes a 404 per jar.
  Anything from Modrinth is recoverable by installing it again. A hand-uploaded jar is gone unless
  HOPPER recognised it on Modrinth by its hash.
- **Bytes without rows.** The server has no mods. The reclaim sweep refuses to run in this state
  rather than deleting every jar, so the store survives the mistake.

### Taking one

Stopping the stack and copying both volumes is the simplest correct backup. To take one while it
runs, dump the database first, then the blobs:

```bash
docker compose exec -T db pg_dump -U hopper -Fc hopper > hopper-$(date +%F).dump
docker run --rm -v hopper-data:/data -v "$PWD":/out alpine \
  tar czf /out/hopper-data-$(date +%F).tar.gz -C /data .
```

::: warning Order matters
Dump the database first, blobs second. A mod added in between then leaves a blob with no row, which
is the harmless failure. The other order leaves a row with no blob, which breaks clients.
:::

The tar takes `keys/` along with `blobs/`, which is what you want. With an external Postgres, run
`pg_dump` against it as usual and back up `hopper-data` the same way.

Blobs are content-addressed and never rewritten, so `blobs/` only grows between sweeps. It rsyncs
incrementally.

### Putting it back

Restore the database, restore the volume, start HOPPER. It migrates at boot, so a dump from an older
version needs no separate step.

Then check it worked. Open the Mods page for a server: anything whose jar did not come back carries
a **Bytes missing** badge. A good restore is one where no server shows one.

If the reclaim sweep logs that it is refusing to run, the database came back empty or points
somewhere else. Fix that first. It is the guard telling you the two halves do not match.

## Upgrading

The API migrates the database at boot. An upgrade is a `docker compose pull` and a restart, with no
separate migration step.
