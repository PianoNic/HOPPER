# What is HOPPER?

HOPPER is a self-hosted web application that keeps every player on a Minecraft server running the
same mods.

Everyone on a server needs the same mod set. Normally that means zipping a folder, posting it in
Discord, and chasing the one person who missed an update. HOPPER makes the server the source of
truth and has each player's game pull from it at launch.

- **One list, every client**. Add a mod in the dashboard and it is on every client the next time
  they start. On Forge and NeoForge, in the same launch - no restart.
- **One jar, once**. Players install a single jar that already carries its server's URL and token.
  It works under Prism, CurseForge and the vanilla launcher alike.
- **Your dedicated server too**. The same jar keeps it in sync from the same list.
- **Sides are respected**. A minimap belongs on clients, a permissions mod on the server. Mark
  which, and each side only receives what belongs to it.
- **Your own mods are left alone**. Downloads land in `hoppermods/`, never in `mods/`. HOPPER never
  deletes a jar it did not download.
- **Nothing blocks the launch**. Offline, or the server is down, the game starts on the last good
  set.

There is no limit to the number of servers, mods or players, and there never will be.

## Architecture

HOPPER is two things: a server you run, and a jar your players install.

| Component | Role |
| --- | --- |
| **The server** | Web application hosting the dashboard, the API and the mod blobs. It is the source of truth for what each server should run. |
| **The locator** | A small jar in each client's `mods/`. It reads the manifest, downloads what is missing, and hands the jars to the loader. |

The locator is one adapter per loader generation over a shared core. Only the adapter knows what
loader it is running under, which is what makes supporting a new one cheap. See
[the locator build](/locator).

## The manifest

The client reads one endpoint, `GET /api/manifest`, and gets back the mod set for its side:

```json
{ "mods": [{ "file": "...", "url": "...", "sha256": "...", "size": 0 }] }
```

That shape is a fixed contract. A jar already sitting in someone's `mods/` keeps working across
server upgrades, because those four fields never change name, order or type.

::: tip
Every download is checked against its SHA-256 before it is installed. A jar that does not match is
not used.
:::

## Loader support

| Loader | Same launch |
| --- | --- |
| Forge 1.12.x | yes |
| Forge 1.14.4 to 1.16.5 | yes |
| Forge 1.17.1 to 1.18.2 | yes |
| Forge 1.19 and newer | yes |
| NeoForge 21.1 and newer | yes |
| Quilt | no, sync then restart |
| Fabric | no, sync then restart |

Fabric exposes no public hook that runs before mod discovery, so HOPPER syncs from the `preLaunch`
entrypoint and tells the player a restart is needed when something changed. Quilt has the right hook
and still will not load a third-party plugin that uses it, with or without
`-Dloader.experimental.allow_loading_plugins=true`, so a Quilt server gets the Fabric jar and Quilt
runs it through its Fabric compatibility. See [how it works](/how-it-works#loader-coverage).

## Authentication

HOPPER signs users in with OIDC. Bring your own provider - Keycloak, Pocket ID, Authentik - or use
the bundled mock for local development. Administrators are recognised by a role claim, and which
claim that is can be configured to match what your issuer already publishes. See
[Self-hosting](/self-host).

## Next

- [**Self-hosting**](/self-host) - run it with `docker compose`.
- [**How it works**](/how-it-works) - the locator, the generated jar and version coverage.
- [**Developer setup**](/dev-setup) - local dev, migrations, tests.
