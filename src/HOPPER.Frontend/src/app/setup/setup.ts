import { ChangeDetectionStrategy, Component } from '@angular/core';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { CopyButton } from '../shared/components/copy-button/copy-button';

@Component({
  selector: 'app-setup',
  imports: [ContentHeader, CopyButton, HlmCardImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">Client setup</h2>
      </header>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        <div class="mx-auto flex max-w-3xl flex-col gap-4">
          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">1. Install the locator</h3>
              <p hlmCardDescription class="text-xs">
                Drop <code class="font-mono">hopper-&lt;version&gt;.jar</code> into the instance's
                <code class="font-mono">mods/</code> folder. It runs before Forge scans for mods, so
                it can add and remove jars before the game sees them.
              </p>
            </div>
          </section>

          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">2. Write config/hopper.properties</h3>
              <p hlmCardDescription class="text-xs">
                The locator writes this file on first launch with an empty token. Fill the token in
                from the server's <code class="font-mono">Hopper:ClientTokens</code> setting — it is
                deliberately not shown in this dashboard, so hand it over out of band.
              </p>
            </div>
            <div hlmCardContent>
              <div class="flex items-start gap-1">
                <pre
                  class="bg-muted flex-1 overflow-auto rounded-md border p-3 font-mono text-xs leading-relaxed"
                  >{{ properties }}</pre
                >
                <app-copy-button [value]="properties" />
              </div>
            </div>
          </section>

          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">3. Launch</h3>
              <p hlmCardDescription class="text-xs">
                On start the locator fetches the manifest, downloads any jar whose SHA-256 does not
                match what is on disk, and deletes anything the manifest does not list. It then
                reports what it ended up with, which is what fills the
                <strong>Clients</strong> page. A failed sync falls back to the cached jars rather
                than blocking the launch.
              </p>
            </div>
          </section>

          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">Troubleshooting</h3>
            </div>
            <div hlmCardContent class="text-muted-foreground flex flex-col gap-2 text-xs">
              <p>
                <strong class="text-foreground">401 on the manifest</strong> — the token in
                hopper.properties does not match any entry in the server's client-token list.
              </p>
              <p>
                <strong class="text-foreground">"unknown" jars on the Clients page</strong> — the
                player put a jar in the hopper folder by hand, or is carrying one from a mod that
                has since been deleted here. The next sync removes it.
              </p>
              <p>
                <strong class="text-foreground">Client never appears</strong> — reporting is
                best-effort and never blocks the launch, so a client that syncs fine but cannot
                reach <code class="font-mono">/api/clients/report</code> stays invisible here.
              </p>
            </div>
          </section>
        </div>
      </div>
    </section>
  `,
})
export class Setup {
  // Built from the browser's own origin so a copy-paste works for the ordinary same-origin
  // deployment. The token is left as a placeholder on purpose: it lives in server configuration
  // and is never served over HTTP.
  protected readonly properties = [
    '# HOPPER client configuration',
    'enabled=true',
    `manifestUrl=${window.location.origin}/api/manifest`,
    'token=<your-client-token>',
    '',
  ].join('\n');
}
