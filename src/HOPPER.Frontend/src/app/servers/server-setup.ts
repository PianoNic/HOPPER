import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import { lucideDownload, lucideEye, lucideEyeOff, lucideRotateCw } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { messageFrom } from '../shared/utils/format';
import { downloadBlob, messageFromBlobError } from '../shared/utils/download';
import { ServersService } from '../api/api/servers.service';
import { ServerDto } from '../api/model/serverDto';
import { serverIdSignal } from './server-route';

@Component({
  selector: 'app-server-setup',
  imports: [ContentHeader, CopyButton, NgIcon, HlmButtonImports,
    ButtonLoading, HlmCardImports],
  providers: [provideIcons({ lucideDownload, lucideEye, lucideEyeOff, lucideRotateCw })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">{{ serverName() }}</span>
    </app-content-header>

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">Setup</h2>
      </header>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        <div class="mx-auto flex max-w-3xl flex-col gap-4">
          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">1. Download this server's jar</h3>
              <p hlmCardDescription class="text-xs">
                The jar already knows this server's manifest URL and token - HOPPER writes them into
                a <code class="font-mono">hopper-server.properties</code> entry inside a copy of the
                template as you download it. Drop it in the instance's
                <code class="font-mono">mods/</code> folder and launch; there is nothing to
                configure. It runs before Forge scans for mods, so it can add and remove jars before
                the game sees them.
              </p>
              <p hlmCardDescription class="text-xs">
                The same file goes on a dedicated server. There is no separate server download - the
                jar asks the loader which side it is running on and requests the matching set, so a
                mod marked <strong>Client only</strong> never reaches the server and one marked
                <strong>Server only</strong> never reaches a player.
              </p>
            </div>
            <div hlmCardContent>
              <button hlmBtn size="sm" type="button" [disabled]="building()" (click)="downloadJar()" [loading]="building()">
                <ng-icon name="lucideDownload" size="14" />
                {{ building() ? 'Building' : jarName() }}
              </button>
            </div>
          </section>

          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">2. Manual fallback</h3>
              <p hlmCardDescription class="text-xs">
                Only needed for a client running a jar built by hand rather than downloaded above.
                The locator writes <code class="font-mono">config/hopper.properties</code> on first
                launch; anything the jar already carries wins over what is in that file, so it is
                also where <code class="font-mono">enabled=false</code> goes when a player wants to
                stop syncing without touching the jar.
              </p>
            </div>
            <div hlmCardContent class="flex flex-col gap-3">
              <div class="flex items-start gap-1">
                <pre
                  class="bg-muted flex-1 overflow-auto rounded-md border p-3 font-mono text-xs leading-relaxed"
                  >{{ token() ? properties() : maskedProperties() }}</pre
                >
                @if (token()) {
                  <app-copy-button [value]="properties()" />
                }
              </div>
              <div>
                <button
                  [loading]="revealing()"
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  [disabled]="revealing()"
                  (click)="toggleToken()"
                >
                  <ng-icon [name]="token() ? 'lucideEyeOff' : 'lucideEye'" size="14" />
                  {{ revealing() ? 'Fetching' : token() ? 'Hide token' : 'Reveal token' }}
                </button>
              </div>
            </div>
          </section>

          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">3. Launch</h3>
              <p hlmCardDescription class="text-xs">
                On start the locator fetches this server's manifest, downloads any jar whose SHA-256
                does not match what is on disk, and deletes anything the manifest does not list. It
                then reports what it ended up with, which is what fills the
                <strong>Clients</strong> page. A failed sync falls back to the cached jars rather
                than blocking the launch.
              </p>
            </div>
          </section>

          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">Rotate the token</h3>
              <p hlmCardDescription class="text-xs">
                Mints a new token for this server and invalidates the old one immediately. Every jar
                already handed out for this server stops working until it is downloaded again.
              </p>
            </div>
            <div hlmCardContent>
              <button
                [loading]="rotating()"
                hlmBtn
                variant="destructive"
                size="sm"
                type="button"
                [disabled]="rotating()"
                (click)="rotate()"
              >
                <ng-icon name="lucideRotateCw" size="14" />
                {{ rotating() ? 'Rotating' : 'Rotate token' }}
              </button>
            </div>
          </section>

          <section hlmCard>
            <div hlmCardHeader>
              <h3 hlmCardTitle class="text-sm">Troubleshooting</h3>
            </div>
            <div hlmCardContent class="text-muted-foreground flex flex-col gap-2 text-xs">
              <p>
                <strong class="text-foreground">401 on the manifest</strong> - the token in the jar
                (or in hopper.properties) is not this server's token. It was rotated, or the player
                is holding another server's jar. Download a fresh one.
              </p>
              <p>
                <strong class="text-foreground">"unknown" jars on the Clients page</strong> - the
                player put a jar in the hopper folder by hand, or is carrying one from a mod that
                has since been deleted here. The next sync removes it.
              </p>
              <p>
                <strong class="text-foreground">Client never appears</strong> - reporting is
                best-effort and never blocks the launch, so a client that syncs fine but cannot
                reach <code class="font-mono">/api/clients/report</code> stays invisible here.
              </p>
              <p>
                <strong class="text-foreground">503 on the jar download</strong> - the deployment
                has no locator template for this server's loader. Build them with
                <code class="font-mono">cd src/HOPPER.Locator &amp;&amp; ./gradlew templates</code>
                and point <code class="font-mono">Hopper:LocatorTemplateDirectory</code> at the
                directory it writes. The error names the file it wanted.
              </p>
            </div>
          </section>
        </div>
      </div>
    </section>
  `,
})
export class ServerSetup {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ServersService);
  private readonly confirm = inject(ConfirmService);

  protected readonly serverId = serverIdSignal(this.route);

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly token = signal<string | null>(null);
  protected readonly revealing = signal(false);
  protected readonly rotating = signal(false);
  protected readonly building = signal(false);

  protected readonly serverName = computed(() => this.server()?.name ?? '');
  protected readonly jarName = computed(() => {
    const slug = this.server()?.slug;
    return slug ? `Download ${slug}-hopper.jar` : 'Download jar';
  });

  private readonly manifestUrl = `${window.location.origin}/api/manifest`;

  protected readonly maskedProperties = computed(() => this.build('<reveal the token>'));
  protected readonly properties = computed(() => this.build(this.token() ?? ''));

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id === '') return;

      this.token.set(null);

      this.api.apiServersIdGet(id).subscribe({
        next: (server) => this.server.set(server),
        error: (err) => toast.error(messageFrom(err, 'Failed to load the server')),
      });
    });
  }

  private build(token: string): string {
    return [
      '# HOPPER client configuration',
      'enabled=true',
      `manifestUrl=${this.manifestUrl}`,
      `token=${token}`,
      '',
    ].join('\n');
  }

  protected toggleToken(): void {
    if (this.token() !== null) {
      this.token.set(null);
      return;
    }
    this.reveal();
  }

  private reveal(): void {
    const id = this.serverId();
    if (id === '') return;

    this.revealing.set(true);

    this.api.apiServersIdTokenGet(id).subscribe({
      next: (result) => {
        this.token.set(result.token);
        this.revealing.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to read the client token'));
        this.revealing.set(false);
      },
    });
  }

  protected async rotate(): Promise<void> {
    const id = this.serverId();
    if (id === '') return;

    const ok = await this.confirm.open({
      title: 'Rotate this server’s token?',
      message:
        'The current token stops working the moment this completes. Every player holding a jar for this server has to download a new one before their next launch will sync.',
      confirmLabel: 'Rotate',
      destructive: true,
    });
    if (!ok) return;

    this.rotating.set(true);
    this.api.apiServersIdTokenPost(id).subscribe({
      next: (result) => {
        this.token.set(result.token);
        this.rotating.set(false);
        toast.success('Token rotated. Hand out a fresh jar.');
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to rotate the token'));
        this.rotating.set(false);
      },
    });
  }

  protected downloadJar(): void {
    const server = this.server();
    if (!server) return;

    this.building.set(true);

    this.api.apiServersIdJarGet(server.id).subscribe({
      next: (jar) => {
        downloadBlob(jar as unknown as Blob, `${server.slug}-hopper.jar`);
        this.building.set(false);
      },
      error: async (err) => {
        toast.error(await messageFromBlobError(err, 'Failed to build the jar'));
        this.building.set(false);
      },
    });
  }
}
