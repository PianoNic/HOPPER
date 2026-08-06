import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import {
  lucideDownload,
  lucideHardDrive,
  lucidePackage,
  lucideRefreshCw,
  lucideTriangleAlert,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { formatBytes, messageFrom, toNumber } from '../shared/utils/format';
import { downloadBlob, messageFromBlobError } from '../shared/utils/download';
import { ServersService } from '../api/api/servers.service';
import { ServerClientsService } from '../api/api/serverClients.service';
import { ServerModsService } from '../api/api/serverMods.service';
import { ClientDto } from '../api/model/clientDto';
import { ModDto } from '../api/model/modDto';
import { ServerDto } from '../api/model/serverDto';
import { serverIdSignal } from './server-route';

const ACTIVE_WINDOW_MS = 24 * 60 * 60 * 1000;

@Component({
  selector: 'app-server-overview',
  imports: [
    ContentHeader,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
  ],
  providers: [
    provideIcons({
      lucideDownload,
      lucideHardDrive,
      lucidePackage,
      lucideRefreshCw,
      lucideTriangleAlert,
      lucideUsers,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">{{ serverName() }}</span>
    </app-content-header>

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">Overview</h2>
        <div class="flex items-center gap-2">
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            (click)="reload()"
            [disabled]="loading()"
          >
            <ng-icon name="lucideRefreshCw" size="14" />
            {{ loading() ? 'Loading…' : 'Refresh' }}
          </button>
          <button hlmBtn size="sm" type="button" [disabled]="building()" (click)="downloadJar()">
            <ng-icon name="lucideDownload" size="14" />
            {{ building() ? 'Building…' : 'Download client jar' }}
          </button>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          @for (stat of stats(); track stat.label) {
            <section hlmCard>
              <div hlmCardHeader class="flex flex-row items-center justify-between gap-2 pb-2">
                <h3 hlmCardDescription class="text-xs">{{ stat.label }}</h3>
                <ng-icon [name]="stat.icon" size="16" class="text-muted-foreground" />
              </div>
              <div hlmCardContent>
                <p class="text-2xl font-semibold tabular-nums">{{ stat.value }}</p>
                <p class="text-muted-foreground mt-1 text-xs">{{ stat.hint }}</p>
              </div>
            </section>
          }
        </div>

      </div>
    </section>
  `,
})
export class ServerOverview {
  private readonly route = inject(ActivatedRoute);
  private readonly serversApi = inject(ServersService);
  private readonly modsApi = inject(ServerModsService);
  private readonly clientsApi = inject(ServerClientsService);

  protected readonly serverId = serverIdSignal(this.route);

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly mods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly clients = signal<ReadonlyArray<ClientDto>>([]);
  protected readonly loading = signal(false);
  protected readonly building = signal(false);

  protected readonly serverName = computed(() => this.server()?.name ?? '');

  protected readonly stats = computed(() => {
    const mods = this.mods();
    const clients = this.clients();
    const totalBytes = mods.reduce((sum, m) => sum + toNumber(m.size), 0);

    const cutoff = Date.now() - ACTIVE_WINDOW_MS;
    const active = clients.filter((c) => Date.parse(c.lastSeenAt) >= cutoff);

    const distinct = new Set(mods.map((m) => m.sha256));
    const drifting = active.filter((c) => {
      const reported = new Set(c.mods.map((m) => m.sha256));
      const missing = mods.some((m) => !reported.has(m.sha256));
      const unknown = c.mods.some((m) => !m.known);
      return missing || unknown;
    });

    return [
      {
        label: 'Mods served',
        value: `${mods.length}`,
        hint: `${distinct.size} distinct blob${distinct.size === 1 ? '' : 's'}`,
        icon: 'lucidePackage',
      },
      {
        label: 'Distributed size',
        value: formatBytes(totalBytes),
        hint: 'Downloaded once per client, then cached by hash',
        icon: 'lucideHardDrive',
      },
      {
        label: 'Clients (24h)',
        value: `${active.length}`,
        hint: `${clients.length} known in total`,
        icon: 'lucideUsers',
      },
      {
        label: 'Showing drift',
        value: `${drifting.length}`,
        hint:
          drifting.length === 0
            ? 'Every recent client matches the manifest'
            : 'Missing or unrecognised jars on disk',
        icon: 'lucideTriangleAlert',
      },
    ];
  });

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id !== '') this.load(id);
    });
  }

  protected reload(): void {
    const id = this.serverId();
    if (id !== '') this.load(id);
  }

  private load(id: string): void {
    this.loading.set(true);

    forkJoin({
      server: this.serversApi.apiServersIdGet(id),
      mods: this.modsApi.apiServersIdModsGet(id),
      clients: this.clientsApi.apiServersIdClientsGet(id),
    }).subscribe({
      next: (result) => {
        this.server.set(result.server);
        this.mods.set(result.mods);
        this.clients.set(result.clients);
        this.loading.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load the overview'));
        this.loading.set(false);
      },
    });
  }

  protected downloadJar(): void {
    const server = this.server();
    if (!server) return;

    this.building.set(true);

    this.serversApi.apiServersIdJarGet(server.id).subscribe({
      next: (jar) => {
        downloadBlob(jar as unknown as Blob, `${server.slug}-hopper.jar`);
        this.building.set(false);
      },
      error: async (err) => {
        toast.error(await messageFromBlobError(err, 'Failed to build the client jar'));
        this.building.set(false);
      },
    });
  }
}
