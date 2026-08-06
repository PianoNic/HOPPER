import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import {
  lucideArrowRight,
  lucidePackage,
  lucideRefreshCw,
  lucideServer,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { messageFrom, toNumber } from '../shared/utils/format';
import { ServersService } from '../api/api/servers.service';
import { ServerDto } from '../api/model/serverDto';

/**
 * The landing page: everything across every server at a glance.
 *
 * Deliberately built from the one server list endpoint rather than fanning out per server. The list
 * already carries mod and client counts, so a deployment with twenty servers still costs one
 * request; per-server drift lives on the server's own overview, where the data to compute it is
 * already being fetched.
 */
@Component({
  selector: 'app-home',
  imports: [
    ContentHeader,
    RouterLink,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
  ],
  providers: [
    provideIcons({
      lucideArrowRight,
      lucidePackage,
      lucideRefreshCw,
      lucideServer,
      lucideUsers,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">Home</span>
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
            (click)="load()"
            [disabled]="loading()"
          >
            <ng-icon name="lucideRefreshCw" size="14" />
            {{ loading() ? 'Loading…' : 'Refresh' }}
          </button>
          <a hlmBtn size="sm" routerLink="/servers">
            All servers
            <ng-icon name="lucideArrowRight" size="14" />
          </a>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        <div class="grid gap-3 sm:grid-cols-3">
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

        @if (servers().length === 0 && !loading()) {
          <div
            class="text-muted-foreground flex h-full flex-col items-center justify-center gap-2 p-10 text-center text-sm"
          >
            <ng-icon name="lucideServer" size="28" class="opacity-60" />
            <p>No servers yet.</p>
            <p class="max-w-md text-xs">
              A server is one mod list plus one client token. Create the first one on the
              <a routerLink="/servers" class="underline">Servers</a> page, then hand out the jar it
              generates.
            </p>
          </div>
        }
      </div>
    </section>
  `,
})
export class Home {
  private readonly api = inject(ServersService);

  protected readonly servers = signal<ReadonlyArray<ServerDto>>([]);
  protected readonly loading = signal(false);

  constructor() {
    this.load();
  }

  protected readonly stats = computed(() => {
    const servers = this.servers();
    const mods = servers.reduce((sum, s) => sum + toNumber(s.modCount), 0);
    const clients = servers.reduce((sum, s) => sum + toNumber(s.clientCount), 0);

    return [
      {
        label: 'Servers',
        value: `${servers.length}`,
        hint: servers.length === 1 ? 'One mod list' : 'Each with its own mod list and token',
        icon: 'lucideServer',
      },
      {
        // Summed across servers, so a jar on two servers counts twice: this is how many entries are
        // being served, not how much is stored. The blob store deduplicates by hash underneath.
        label: 'Mods served',
        value: `${mods}`,
        hint: 'Across all servers, counted per server',
        icon: 'lucidePackage',
      },
      {
        label: 'Known clients',
        value: `${clients}`,
        hint: 'Every client that has ever reported in',
        icon: 'lucideUsers',
      },
    ];
  });

  protected load(): void {
    this.loading.set(true);
    this.api.apiServersGet().subscribe({
      next: (servers) => {
        this.servers.set(servers);
        this.loading.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load the overview'));
        this.loading.set(false);
      },
    });
  }
}
