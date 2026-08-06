<p align="center">
  <img src="assets/hopper-icon.svg" width="180" alt="HOPPER Logo" />
</p>
<p align="center">
  <strong>HOPPER</strong><br/>
  One list on the server. Every client in sync, before the game starts.
</p>
<p align="center">
  <a href="https://github.com/PianoNic/HOPPER"><img src="https://badgetrack.pianonic.ch/badge?tag=hopper&label=visits&color=0b1220&style=flat" alt="visits" /></a>
  <a href="docs/self-host.md"><img src="https://img.shields.io/badge/Self--Host-Instructions-0b1220.svg" alt="Self-hosting" /></a>
  <img src="https://img.shields.io/badge/.NET-10-0b1220.svg" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Angular-21-0b1220.svg" alt="Angular 21" />
</p>

---

> **Heads up:** HOPPER is in early development. Expect rough edges and breaking changes between versions.

## What is HOPPER?

HOPPER keeps every player's mods in sync with one list you control on the server. Add a mod in the dashboard, and it is on every client the next time they launch - no zip files in Discord, no "did you update yet?", and no restart. Your friends install a single jar once, and it works under Prism, CurseForge and the vanilla launcher alike.

## Features

- **No restart**: mods are downloaded before Forge scans for them, so they load in the same launch. See [how it works](docs/how-it-works.md).
- **Launcher-agnostic**: one jar in `mods/`, no pre-launch command and no custom JVM arguments.
- **Zero client config**: the generated jar already carries its server's URL and token.
- **Multiple servers**: each with its own mod list, token and jar; a client only ever sees its own.
- **Pack import**: Modrinth `.mrpack`, CurseForge and Prism exports, by file or by URL.
- **Verified downloads**: every jar is checked against its SHA-256 before it is installed.
- **Never blocks the launch**: offline or server down, the game starts on the last good set.
- **OIDC auth**: bring your own provider or use the bundled Keycloak-compatible mock.

## Get started

- 📦 **[Self-hosting guide](docs/self-host.md)** - run the image with `docker compose`.
- 🛠️ **[Developer setup](docs/dev-setup.md)** - local dev with `dotnet run` + Bun, migrations, tests.
- 🧩 **[How it works](docs/how-it-works.md)** - the locator, the generated jar, and version coverage.

<details>
<summary><strong>Tech stack</strong></summary>

- **.NET 10** ASP.NET Core API (Mediator, EF Core, Clean Architecture).
- **Angular 21** + Signals + Spartan UI.
- **PostgreSQL** via Npgsql; **Testcontainers** so tests run on the engine that ships.
- **Java 17** Forge mod locator, built with Gradle and patched per server as a plain zip.
- **TUnit** + **vitest** for tests; **OpenAPI** client via `bun run apigen`.

</details>

## License

TBD.

---

<p align="center">Made with care by <a href="https://github.com/PianoNic">PianoNic</a></p>
