# HOPPER.Frontend — house style

Angular 21 standalone, bun 1.3, Tailwind v4, Spartan UI. Mirrors KRINT.Frontend; when in doubt,
look at how KRINT does it rather than inventing something.

## Components

- Standalone everywhere. Never write `standalone: true` — it is the default in v20+.
- `ChangeDetectionStrategy.OnPush` on every component.
- `inject()` only. No constructor parameter injection.
- `signal()` / `computed()` for state. `.set()` / `.update()`, never `mutate`.
- Native control flow `@if` / `@for` / `@switch`. No `*ngIf`, no `ngClass` / `ngStyle` — bind
  `class` / `style` instead.
- Inline `template:` for pages; `templateUrl` only once a template gets long (sidenav, app-layout,
  content-header).
- `protected readonly` for anything the template touches, `private readonly` for injected services.
- No arrow functions in templates, and no globals like `new Date()` — pass a `now` signal in.
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

**A new route's host selector must be added to the `app-home, app-mods, app-clients, app-setup` rule
in `src/styles.css`.** Without it the host is `display: block` with auto height and every `flex-1`
inside measures against the wrong box.

## API client

`src/app/api/` is generated and committed. Regenerate it, never hand-edit it:

```bash
dotnet run --project ../HOPPER.API --launch-profile http   # must be up on :5170 first
bun run apigen
```

Pages inject the generated services directly (`ModsService`, `ClientsService`, `AppService`) — there
is no hand-written wrapper layer.

Two generator quirks to expect:

- .NET `long` / `int` come through as an opaque `ManifestModDtoSize` interface. Coerce with
  `toNumber()` from `shared/utils/format.ts`; do not add `as any` at the call site.
- Error bodies are HOPPER's `{ "error": "…" }`, not `ProblemDetails`. Read them with
  `messageFrom()` from the same file.

## Auth

OIDC via `angular-auth-oidc-client`, configured from `GET /api/app` at bootstrap
(`shared/auth/auth.config.ts`) rather than from `environment.ts`, so one built bundle works against
whatever IdP the server points at. `authInterceptor()` attaches the token to `secureRoutes`;
`autoLoginPartialRoutesGuard` guards every child route.

The dashboard never touches `/api/manifest`, `/api/blobs/*` or `/api/clients/report` — those take the
shared client token, which belongs to the Forge locator and is deliberately never served to the SPA.
The generated client has methods for them; ignore those methods.

## Commands

```bash
bun install
bun run apigen     # regenerate the typed client (API must be running on :5170)
bun start          # ng serve -> http://localhost:4200
bun run build      # production by default -> dist/
bun run build:agent
bun test           # vitest
```

Note: `bun test` finds no specs when the checkout path contains parentheses (e.g. `files (7)`) —
vitest reads them as a glob group. Move or symlink the repo to a paren-free path to run tests.

## Misc

- No AI attribution anywhere — commits, PRs, comments.
- Comments explain *why*, in full sentences, and usually name the failure mode being avoided.
