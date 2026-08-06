# HOPPER.Frontend - house style

Angular 21 standalone, bun 1.3, Tailwind v4, Spartan UI. Mirrors KRINT.Frontend; when in doubt,
look at how KRINT does it rather than inventing something.

## Components

- Standalone everywhere. Never write `standalone: true` - it is the default in v20+.
- `ChangeDetectionStrategy.OnPush` on every component.
- `inject()` only. No constructor parameter injection.
- `signal()` / `computed()` for state. `.set()` / `.update()`, never `mutate`.
- Native control flow `@if` / `@for` / `@switch`. No `*ngIf`, no `ngClass` / `ngStyle` - bind
  `class` / `style` instead.
- Inline `template:` for pages; `templateUrl` only once a template gets long (sidenav, app-layout,
  content-header).
- `protected readonly` for anything the template touches, `private readonly` for injected services.
- No arrow functions in templates, and no globals like `new Date()` - pass a `now` signal in.
- Class names are bare PascalCase with no suffix (`Mods`, `Clients`, `AppLayout`). Files are
  kebab-case with no `.component.ts`. Selectors are `app-<kebab>`.

## Layout contract

The page itself never scrolls. `html, body` are locked to the viewport in `src/styles.css`, and each
route owns one inner scroll surface:

```html
<app-content-header />
<section class="flex flex-1 min-h-0 flex-col border-t">
  <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">…</header>
  <div class="min-h-0 flex-1 overflow-auto px-4">…</div>
</section>
```

**A new route's host selector must be added to the `app-servers, app-server-overview,
app-server-mods, app-server-clients, app-server-setup` rule in `src/styles.css`.** Without it the
host is `display: block` with auto height and every `flex-1` inside measures against the wrong box.

## Routing

The dashboard is a list of servers first and one server's pages second:

```
/                       Servers        create / rename / delete / copy token / download jar
/servers/:id            ServerOverview
/servers/:id/mods       ServerMods
/servers/:id/clients    ServerClients
/servers/:id/pending    ServerPending  the jars an import could not fetch: supply or drop each one
/servers/:id/setup      ServerSetup    reveal token, rotate token, download jar
```

Every per-server page reads its `:id` with `serverIdSignal()` from `servers/server-route.ts`, never
off `route.snapshot`. The router reuses a component when only a path parameter changes, so a
snapshot read leaves one server's page showing the previous server's rows.

The sidenav's per-server section is derived from the URL (`sidenav.ts`, `SERVER_ROUTE`) and is not
rendered at all outside `/servers/:id`. Items that prefix-match their own children - `/` and a
server's overview - pass `exact: true` to `isRouteActive`.

## API client

`src/app/api/` is generated and committed. Regenerate it, never hand-edit it:

```bash
dotnet run --project ../HOPPER.API --launch-profile http   # must be up on :5170 first
bun run apigen
```

Pages inject the generated services directly (`ServersService`, `ServerModsService`,
`ServerClientsService`, `ServerImportsService`, `AppService`) - there is no hand-written wrapper
layer.

Generator quirks to expect:

- .NET `long` / `int` come through as opaque interfaces (`ManifestModDtoSize`,
  `ModImportDtoImportedCount`) - this now hits `ServerDto.modCount` and `clientCount` too. Coerce
  with `toNumber()` from `shared/utils/format.ts`; do not add `as any` at the call site.
- Error bodies are HOPPER's `{ "error": "…" }`, not `ProblemDetails`. Read them with
  `messageFrom()` from the same file.
- `apiServersIdJarGet` is declared `Observable<FileResult>` but runs with `responseType: 'blob'`,
  because `application/java-archive` is neither a text nor a JSON mime. The value is a Blob;
  `as unknown as Blob` at the call site is the only honest way to say so. Its *errors* are Blobs
  too, so `messageFrom()` cannot read them - use `messageFromBlobError()` from
  `shared/utils/download.ts`, or the 503 that names `Hopper:LocatorTemplatePath` is lost.
- `apiServersIdImportsPost` is emitted as multipart-only, with the uploaded file as its one
  parameter. The endpoint also accepts `{"url":"…"}` as JSON - it dispatches on content type - but
  the generator collapses `[Consumes(...)]` to a single shape, so there is no `url` argument and
  calling it with no file sends an empty multipart body the server answers with 400. The URL half
  is therefore the one call in the dashboard written with `HttpClient` directly, in
  `servers/import-pack-dialog.ts`. It is not the start of a wrapper layer; leave it at one call.
- Regenerating does not delete files for routes that no longer exist. Check
  `api/.openapi-generator/FILES` against the directory and remove the leftovers by hand.

## Auth

OIDC via `angular-auth-oidc-client`, configured from `GET /api/app` at bootstrap
(`shared/auth/auth.config.ts`) rather than from `environment.ts`, so one built bundle works against
whatever IdP the server points at. `authInterceptor()` attaches the token to `secureRoutes`;
`autoLoginPartialRoutesGuard` guards every child route.

The dashboard never touches `/api/manifest`, `/api/blobs/*` or `/api/clients/report` - those take a
server's client token, which belongs to the Forge locator. The generated client has methods for
them; ignore those methods.

A server's token is served to the SPA, but only by `GET /api/servers/{id}/token` and only on an
explicit click: the copy-token action on the Servers page and the reveal button on Setup. `ServerDto`
carries no token on purpose, so nothing the dashboard fetches on page load contains one. Do not
fetch it into a resolver, a cache or a list.

## Commands

```bash
bun install
bun run apigen     # regenerate the typed client (API must be running on :5170)
bun start          # ng serve -> http://localhost:4200
bun run build      # production by default -> dist/
bun run build:agent
bun test           # vitest
```

Note: `bun test` finds no specs when the checkout path contains parentheses (e.g. `files (7)`) -
vitest reads them as a glob group. Move or symlink the repo to a paren-free path to run tests.

## Misc

- No AI attribution anywhere - commits, PRs, comments.
- Comments explain *why*, in full sentences, and usually name the failure mode being avoided.
