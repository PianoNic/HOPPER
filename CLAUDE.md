# HOPPER - working conventions

## Workflow (enforced)

Never work on `main`. Every change goes:

1. **Issue**: `gh issue create` with at least one label from `gh label list`.
2. **Branch**: `feature/<issue#>_PascalCase` for new work, refactors and docs, `fix/<issue#>_PascalCase` for bugs.
3. **PR**: `gh pr create` with at least one label.
4. **Squash-merge + delete branch**, then `git fetch --prune && git reset --hard origin/main`.

Branch naming:

- ✅ `feature/12_ModrinthBrowser`
- ✅ `fix/8_BlobOrphanCollection`

## Commits

Past-tense imperative, verb first, one short subject line:

- `Added per-server client tokens`
- `Fixed the Postgres 18 volume path in compose.yml`
- `Removed the redundant manifest card from the server overview`

No AI attribution of any kind. No `Co-Authored-By:` trailers, no `🤖 Generated with...`, nothing.

## PRs

Title mirrors the commit. Body is a one-line summary in commit-subject style, then `Closes #<issue>`.
The commits already say what changed, so the PR does not repeat them. No test plans, no checklists,
no headers.

```
Added the Modrinth browser with dependency resolution

Closes #11
```

Labels: `feature`, `enhancement`, `bug`, `refactor`, `documentation`, `CI/CD`. Multiple with
`--label feature,documentation`.

## Writing

- **Never use em dashes.** Not in code, not in comments, not in UI strings, not in docs. Use " - ".
- The dashboard has **no inline error messages**. Every failure is surfaced with `toast.error`.

## CLI generators

Use them whenever one exists: `gh issue create`, `gh pr create`, `dotnet new`,
`dotnet ef migrations add`, `ng generate @spartan-ng/cli:ui <name>` for a new Spartan component.

## Local dev setup

- `docker compose -f compose.yml -f compose.demo.yml` runs the whole stack with a
  `mock-oauth2-server` instead of Keycloak. Dashboard on `:58722`, IdP on `:58538`. `compose.yml`
  alone starts no IdP: it is the real deployment, and the demo overlay is a separate file so a plain
  `docker compose up -d` cannot pick up a provider that signs a token for anyone who asks.
- `compose.dev.yml` runs only Postgres (`:5433`) and the IdP (`:58539`); the API and dashboard run on
  the host. Postgres is on 5433 on purpose so it cannot collide with one already running.
- Postgres 18+ stores its cluster in a major-version subdirectory. The volume must be mounted at
  `/var/lib/postgresql`, **not** `/var/lib/postgresql/data`, or the container restart-loops with
  nothing ever listening.
- The split dev setup is the only one that needs `Cors:AllowedOrigins`. It is already set, together
  with the `Oidc:*` values, in `src/HOPPER.API/appsettings.Development.json`.
- Regenerate the typed API client after a contract change with `bun run apigen`, with the API running.

## Tests

```bash
dotnet test                                      # needs Docker
cd src/HOPPER.Frontend && bun run test
```

- `dotnet test` needs Docker: the suite starts a throwaway Postgres through Testcontainers rather
  than testing against a different engine than production runs on.
- It also needs `global.json`'s `test.runner` opt-in. TUnit is a Microsoft.Testing.Platform framework
  and the .NET 10 SDK dropped the VSTest bridge, so without it the build fails outright rather than
  reporting no tests.

## Migrations

After pulling upstream:

```bash
dotnet ef migrations has-pending-model-changes --project src/HOPPER.Infrastructure --startup-project src/HOPPER.API
```

If pending, remove any stale local migration and regenerate:

```bash
dotnet ef migrations remove --project src/HOPPER.Infrastructure --startup-project src/HOPPER.API --force
dotnet ef migrations add <Name> --project src/HOPPER.Infrastructure --startup-project src/HOPPER.API
```

The API migrates at boot, so `dotnet ef database update` is never needed.

## Things that will bite you

- **The client wire format is fixed.** `GET /api/manifest` must serialize exactly
  `{"mods":[{"file","url","sha256","size"}]}`. A shipped Java client parses it and tests pin it. A
  global JSON naming policy change would silently break every client in the field.
  Adding a field is allowed under two rules and no others: it goes **after** `size`, and it is
  **omitted from the JSON when it has no value**. The shipped client's reader looks up the keys it
  knows by name and ignores the rest, so an extra key is inert to every jar already in the field -
  which stays true only while the four originals keep their names, their order and their types.
  `modIds` (the array of mod ids the client de-duplicates on) is the first such field and is the
  worked example. Never rename, reorder or nullify the four originals.
- **Blob sharing is global on purpose.** `DeleteModCommand`'s orphan check queries `Mods` across all
  servers with no `ServerId` filter. Narrowing it to one server would delete another server's blobs.
- **A new route MUST be added to the routed-host rule in `src/HOPPER.Frontend/src/styles.css`**, or
  its page layout collapses.
- **The locator is loader-specific, the syncer is not.** Only the adapter under `src/HOPPER.Locator/`
  imports loader types. Keep it that way; it is what makes supporting another loader cheap.
- **`Hopper:LocatorTemplateDirectory`** must point at the directory from
  `cd src/HOPPER.Locator && ./gradlew templates`, not at a single jar. The locator is one adapter
  per loader generation and `LocatorTemplates.For` picks one by the server's loader and Minecraft
  version, so the endpoint needs all of them. The Dockerfile builds them in a JDK stage; locally you
  run the task yourself or the jar endpoint 503s and names the file it wanted.
- **The module-to-filename mapping lives in the `templates` task in `src/HOPPER.Locator/build.gradle`.**
  `LocatorTemplates` addresses jars by unversioned name, so a version bump never touches C#. Adding a
  loader means a module, an entry in that task, and an arm in `LocatorTemplates.For` - nothing else.
- **Gradle 8.14.3 will not run on JDK 25.** It fails with "Unsupported class file major version 69".
  Build the locator on JDK 21 or 24; the Dockerfile pins `eclipse-temurin:21-jdk` for this reason.
- **`LocatorJarBuilder` rewrites the jar; it must never use `ZipArchiveMode.Update`.** The JDK's
  `jar` tool writes entries with general-purpose bit 3 set, putting sizes in trailing data
  descriptors. .NET's updater drops those but does not subtract them from the central directory, so
  every entry after the first compressed one is recorded 16 bytes off. .NET still reads it;
  `java.util.zip` answers "invalid LOC header" and Forge silently skips the jar.
  `LocatorJarBuilderTests` pins the byte-level invariant.
- **`MapInboundClaims = false` in `AuthExtensions` is load-bearing.** The default map rewrites
  `roles` to the WS-* role URI before `RoleClaimType` is matched, so with it on, a token carrying
  `hopper-admin` fails the policy and every admin request 403s with the claim visible in the token.
- **Compose interpolation, not literals, in `compose.yml`.** An inline `environment:` value outranks
  `env_file`, so a literal silently beats the operator's `.env`. Every value is `${VAR:-default}`.
  Unset variables arrive as empty strings, not absent keys, so config reads treat blank as unset.
- **Postgres 18+ mounts at `/var/lib/postgresql`,** not `/var/lib/postgresql/data`. The wrong path
  restart-loops with nothing ever listening.
- **`.gitattributes` pins `gradlew` to LF.** Under `core.autocrlf` a CRLF shebang makes the Docker
  locator stage fail with `./gradlew: not found`.
- **An ETag set on `Response.Headers` does nothing.** ASP.NET only answers `If-None-Match` with a 304,
  and `Range` with a 206, when the tag is handed to the `File(...)` overload that takes an
  `EntityTagHeaderValue` and `enableRangeProcessing: true`. Setting the header by hand advertises a
  revalidation that never happens: the blob endpoint did exactly that and returned the whole jar to
  every conditional request, and no download could resume.
- **`docs/locator.md` holds the locator build's reasoning**, which the `build.gradle` files used to
  carry as comments: the version floor and ceiling of every adapter, checked against real loader
  artifacts, what each one may and may not touch, why `--release` rather than a toolchain, and why
  `-Werror` runs with exactly two lint categories off. Read it before changing anything under
  `src/HOPPER.Locator/`, and put what you learn back into it rather than into a comment.
- **Comments:** keep them to an absolute minimum in every language. Prefer naming and structure. The
  bullets above, `docs/locator.md` and the rest of `docs/` exist so the code does not have to carry
  them. Generated trees are out of scope and stay as their generator writes them:
  `src/HOPPER.Frontend/libs/` (Spartan), `src/HOPPER.Frontend/src/app/api/` (`bun run apigen`) and
  `src/HOPPER.Infrastructure/Migrations/` (`dotnet ef`).

## Before you claim it works

- `dotnet build` is 0 warnings, 0 errors.
- `dotnet test` and `bun run test` are both green.
- `bun run build` produces no `NG` warnings.
- **Never launch Minecraft or a launcher to verify.** Drive the Java classes directly instead.
