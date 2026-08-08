# Locator end-to-end verification

Starts a real Minecraft client for every locator adapter and checks that HOPPER actually did its
job inside it. This is the check behind "the locator works on Forge 1.12.2" - nothing else in the
repository can make that claim, because nothing else runs a loader.

It is a developer harness, not a test suite. It needs Prism Launcher, a signed-in Minecraft account
and the network, so CI cannot run it. Run it before a release, or after touching anything under
`src/HOPPER.Locator/`.

## What it does per adapter

1. Writes a throwaway Prism instance - `instance.cfg` and `mmc-pack.json`, nothing downloaded by
   hand. Prism resolves the Minecraft and loader components itself on first launch.
2. Creates a HOPPER server for that loader and Minecraft version, and installs one mod from
   Modrinth into it.
3. Downloads the configured jar from `GET /api/servers/{id}/jar` and drops it in `mods/`.
4. Launches the client, waits until the log says the game is running, and closes it.
5. Reads the log and reports three things:

| Column | Means |
| --- | --- |
| `started` | The client reached a running state rather than a crash report |
| `synced` | HOPPER contacted the server and reported the mods ready |
| `identity` | The loader listed HOPPER by name and version |

`identity` passes automatically on the Forge family. Those jars sit on the service layer, which the
mod scanner skips by design, so no Forge or NeoForge version can list them - `docs/locator.md` has
the source for each. Only Fabric and Quilt parse the descriptor, and there the check is real.

## Running it

The API has to be up with the locator templates built:

```bash
cd src/HOPPER.Locator && ./gradlew templates
cd ../HOPPER.API && dotnet run
```

Then, with a bearer token for an account in the admin role:

```bash
export HOPPER_E2E_TOKEN=...
python tools/locator-e2e/verify.py                 # all seven adapters
python tools/locator-e2e/verify.py Fabric          # just one
python tools/locator-e2e/verify.py --keep          # leave the instances and servers behind
```

Against the dev stack from `compose.dev.yml`, the throwaway IdP hands out a token without a login
form:

```bash
export HOPPER_E2E_TOKEN=$(curl -s -X POST http://localhost:58539/default/token \
  -d grant_type=client_credentials -d client_id=hopper -d client_secret=x \
  -d "scope=openid profile email roles" | python -c "import json,sys; print(json.load(sys.stdin)['access_token'])")
```

Exit code is 0 only when every adapter started, synced and identified itself.

| Setting | Default |
| --- | --- |
| `HOPPER_E2E_TOKEN` | required |
| `HOPPER_E2E_API` | `http://localhost:5170` |
| `HOPPER_E2E_PRISM_INSTANCES` | `%APPDATA%\PrismLauncher\instances` |
| `HOPPER_E2E_PRISM_EXE` | `%LOCALAPPDATA%\Programs\PrismLauncher\prismlauncher.exe` |

## Things worth knowing

- **It closes the client itself.** The wait ends as soon as the log says the game is running; it
  never leaves a Minecraft on your desktop, including when a step throws.
- **It kills every `java`, `javaw` and `prismlauncher` process** when it closes one, so do not run
  it while you are playing.
- **Instances are named `HOPPER-V-<adapter>` and deleted afterwards** unless you pass `--keep`. It
  never touches an instance it did not create.
- **A Quilt server downloads the Fabric jar.** That is the product behaviour, not a gap in the
  harness: Quilt Loader refuses third-party plugins, so HOPPER ships the Fabric jar and lets
  Quilt's Fabric compatibility run it.
- **Loader versions are pinned in `TARGETS`.** When Prism stops offering one, the launch fails at
  component resolution and the row reads `started NO`. Bump it there.
