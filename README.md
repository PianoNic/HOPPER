# <p align="center">HOPPER</p>
<p align="center">
  <img src="./assets/hopper-icon.svg" width="200" alt="HOPPER Logo">
</p>
<p align="center">
  <strong>One list on the server. Every client in sync, before the game starts.</strong>
</p>
<p align="center">
  <a href="https://github.com/PianoNic/HOPPER"><img src="https://badgetrack.pianonic.ch/badge?tag=hopper&label=visits&color=0b1220&style=flat" alt="visits"/></a>
  <a href="https://github.com/PianoNic/HOPPER/releases"><img src="https://img.shields.io/github/v/release/PianoNic/HOPPER?include_prereleases&color=0b1220&label=Latest%20Release"/></a>
  <a href="#-docker--container-registry-usage"><img src="https://img.shields.io/badge/Selfhost-Instructions-0b1220.svg"/></a>
  <a href="./docs/how-it-works.md"><img src="https://img.shields.io/badge/Documentation-Docs-0b1220.svg"/></a>
</p>

> [!WARNING]
> HOPPER is in early development. Expect rough edges and breaking changes between versions.

> [!IMPORTANT]
> This project is **NOT** affiliated with, endorsed by, or connected to Mojang, Microsoft, CurseForge or Modrinth in any way.

## ⚙️ About The Project
HOPPER keeps every player's mods in sync with one list you control on the server. Your friends install a single jar once, and from then on adding a mod is an upload. No zip files in Discord, no "did you update yet?", and no restart: the mods land in the same launch they were downloaded in.

It works under Prism, CurseForge and the vanilla launcher alike, because the one thing every launcher has in common is that it loads `mods/`.

## ✨ Features
- **No restart**: mods are downloaded before Forge scans for them, so they load in the same launch.
- **Launcher-agnostic**: one jar in `mods/`, no pre-launch command and no custom JVM arguments.
- **Zero client config**: the generated jar already carries its server's URL and token.
- **Multiple servers**: each with its own mod list, token and jar. A client only sees its own.
- **Pack import**: Modrinth `.mrpack`, CurseForge and Prism exports, by file or by URL.
- **Verified downloads**: every jar is checked against its SHA-256 before it is installed.
- **Never blocks the launch**: offline or server down, the game starts on the last good set.

## 🛠️ Compatibility
Tested on:
- Forge 1.20.1

Other Forge generations need their own locator build. See [docs/how-it-works.md](./docs/how-it-works.md).

## 🐳 Docker & Container Registry Usage

### Option 1: Run with Docker Compose (Recommended)
**1. Create a `compose.yml` file:**
```yaml
services:
  db:
    image: postgres:18.3
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
    image: ghcr.io/pianonic/hopper:latest
    ports:
      - "58722:8080"
    environment:
      ConnectionStrings__HopperDatabase: "Host=db;Port=5432;Database=hopper;Username=hopper;Password=hopper"
      Hopper__BootstrapClientToken: "change-me"
      Oidc__Authority: "https://id.example.com/realms/hopper"
      Oidc__ClientId: hopper
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

The dashboard is live at [http://localhost:58722](http://localhost:58722).

### Option 2: Clone and build
The repo ships a `compose.yml` with a throwaway IdP included, so a fresh checkout logs in without standing up Keycloak first:
```bash
git clone https://github.com/PianoNic/HOPPER.git
cd HOPPER
docker compose up -d --build
```

## 📦 Install a client
1. Open a server in the dashboard and hit **Download client jar**.
2. Drop `<slug>-hopper.jar` into the player's `mods/` folder.
3. Launch.

There is no step 4. The jar carries its server's id, manifest URL and token, so the player configures nothing.

## ⚙️ Configuration
| Variable | Default | Description |
| --- | --- | --- |
| `ConnectionStrings__HopperDatabase` | compose `db` service | Postgres connection string. HOPPER is Postgres-only. |
| `Blobs__Directory` | `/data/blobs` | Content-addressed jar store, shared across servers. |
| `Hopper__PublicBaseUrl` | derived from the request | Host written into every manifest URL. Leave unset behind a proxy sending `X-Forwarded-*`. |
| `Hopper__BootstrapClientToken` | `change-me` | Token of the `Default` server created on an empty database. |
| `Oidc__Authority` | bundled mock IdP | OIDC issuer for admin access. |
| `CurseForge__ApiKey` | *(unset)* | Optional. Without it, CurseForge imports list unresolvable mods for manual upload. |

`.env.example` documents every setting.

## 🚀 Development
```bash
docker compose -f compose.dev.yml up -d             # Postgres + IdP
dotnet run --project src/HOPPER.API                 # API on :5170
cd src/HOPPER.Frontend && bun install && bun start  # dashboard on :4200
```

```bash
dotnet test                                      # needs Docker (Testcontainers)
cd src/HOPPER.Frontend && bun run test
```

## 🧰 Tech Stack
- .NET 10 + ASP.NET Core (Mediator, EF Core, Clean Architecture)
- Angular 21 + Signals + Spartan UI
- PostgreSQL via Npgsql, Testcontainers for tests
- Java 17 Forge mod locator, built with Gradle

## 📄 License
TBD.

---

<p align="center">Made with care by <a href="https://github.com/PianoNic">PianoNic</a></p>
