# Self-hosting HOPPER

One image holds both halves: the Dockerfile builds the Angular dashboard into the API's `wwwroot`, so the dashboard is always same-origin with the API and there is no CORS allowlist to configure. It also builds the Forge locator template jar in a JDK stage, which is what `GET /api/servers/{id}/jar` patches per server.

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
    image: ghcr.io/pianonic/hopper:latest
    container_name: hopper
    ports:
      - "58722:8080"
    environment:
      ConnectionStrings__HopperDatabase: "Host=db;Port=5432;Database=hopper;Username=hopper;Password=hopper"
      Hopper__BootstrapClientToken: "change-me"
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

The repo's own `compose.yml` bundles a throwaway OIDC provider, so a fresh checkout can log in without standing up Keycloak first. It signs a token for anyone who asks, which is fine on your own machine and nowhere else.

```bash
git clone https://github.com/PianoNic/HOPPER.git
cd HOPPER
docker compose up -d --build
```

Dashboard on `:58722`, the mock IdP on `:58538`. A server named `Default` exists immediately, so you can download a client jar without configuring anything.

## Two credentials, no overlap

- **Admin** (the dashboard, `/api/servers/...`) is OIDC. Point `Oidc__Authority` at your own provider.
- **Client** (`/api/manifest`, `/api/blobs/{sha256}`, `/api/clients/report`) is a per-server token, minted by HOPPER and resolved to a server on every request. A token matching no server is a 401, and a database with no servers rejects every client rather than opening the door.

`Hopper__BootstrapClientToken` only seeds the token of the `Default` server created on an empty database. It is applied while the Servers table is empty and never again, so rotating in the dashboard afterwards sticks.

## Configuration

| Variable | Default | Description |
| --- | --- | --- |
| `ConnectionStrings__HopperDatabase` | compose `db` service | Postgres connection string. HOPPER is Postgres-only. |
| `Blobs__Directory` | `/data/blobs` | Content-addressed jar store. Shared across servers, so a jar used twice is stored once. |
| `Hopper__PublicBaseUrl` | derived from the request | Host written into every manifest URL. Leave unset behind a proxy that sends `X-Forwarded-*`. |
| `Hopper__BootstrapClientToken` | `change-me` | Token of the `Default` server created on an empty database. |
| `Hopper__LocatorTemplatePath` | built into the image | The template jar the download endpoint patches per server. |
| `Oidc__Authority` | bundled mock IdP | OIDC issuer for admin access. |
| `Oidc__InternalAuthority` | unset | Set when the API reaches the IdP on a different address than the browser does. |
| `CurseForge__ApiKey` | *(unset)* | Optional. Without it, CurseForge imports list unresolvable mods for manual upload. |

`.env.example` documents every setting.

## Behind a reverse proxy

Leave `Hopper__PublicBaseUrl` unset if your proxy sends `X-Forwarded-Proto` and `X-Forwarded-Host`; the manifest derives the download host from the request. Set it explicitly when the proxy does not forward them, or when clients dial a different name than the API sees. Getting this wrong means clients receive manifest URLs pointing at the wrong host.

## Upgrading

The API migrates the database at boot, so an upgrade is a `docker compose pull` and a restart. There is no separate migration step.
