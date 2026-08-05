import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import {
  lucideHardDrive,
  lucidePackage,
  lucideRefreshCw,
  lucideTriangleAlert,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { formatBytes, messageFrom, toNumber } from '../shared/utils/format';
import { ClientsService } from '../api/api/clients.service';
import { ModsService } from '../api/api/mods.service';
import { ClientDto } from '../api/model/clientDto';
import { ModDto } from '../api/model/modDto';

const ACTIVE_WINDOW_MS = 24 * 60 * 60 * 1000;

@Component({
  selector: 'app-home',
  imports: [
    ContentHeader,
    CopyButton,
    RouterLink,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
  ],
  providers: [
    provideIcons({
      lucideHardDrive,
      lucidePackage,
      lucideRefreshCw,
      lucideTriangleAlert,
      lucideUsers,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">Overview</h2>
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

        <section hlmCard class="mt-4">
          <div hlmCardHeader>
            <h3 hlmCardTitle class="text-sm">Manifest endpoint</h3>
            <p hlmCardDescription class="text-xs">
              What every client polls on launch. Paste it into hopper.properties as
              <code class="font-mono">manifestUrl</code> — the shared client token goes alongside it
              and is never shown here, because the dashboard has no business holding it.
            </p>
          </div>
          <div hlmCardContent>
            <div class="flex items-center gap-1">
              <code class="bg-muted flex-1 truncate rounded-md border px-3 py-2 font-mono text-xs">
                {{ manifestUrl }}
              </code>
              <app-copy-button [value]="manifestUrl" />
            </div>
            <div class="mt-3 flex gap-2">
              <a hlmBtn variant="outline" size="sm" routerLink="/mods">Manage mods</a>
              <a hlmBtn variant="outline" size="sm" routerLink="/setup">Client setup</a>
            </div>
          </div>
        </section>

      </div>
    </section>
  `,
})
export class Home {
  private readonly clientsApi = inject(ClientsService);
  private readonly modsApi = inject(ModsService);

  protected readonly mods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly clients = signal<ReadonlyArray<ClientDto>>([]);
  protected readonly loading = signal(false);

  // Same-origin in production; in the split dev setup this points at the dev server, which is
  // wrong for a player but right for a copy-paste sanity check. The server's own
  // Hopper:PublicBaseUrl is what actually goes into the manifest.
  protected readonly manifestUrl = `${window.location.origin}/api/manifest`;

  protected readonly stats = computed(() => {
    const mods = this.mods();
    const clients = this.clients();
    const totalBytes = mods.reduce((sum, m) => sum + toNumber(m.size), 0);

    const cutoff = Date.now() - ACTIVE_WINDOW_MS;
    const active = clients.filter((c) => Date.parse(c.lastSeenAt) >= cutoff);

    const required = new Set(mods.map((m) => m.sha256));
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
        hint: `${required.size} distinct blob${required.size === 1 ? '' : 's'}`,
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
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    forkJoin({
      mods: this.modsApi.apiModsGet(),
      clients: this.clientsApi.apiClientsGet(),
    }).subscribe({
      next: (result) => {
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
}
