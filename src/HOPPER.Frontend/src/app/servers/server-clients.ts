import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { forkJoin, interval } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import { lucideRefreshCw, lucideSearch, lucideUsers } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { formatAge, messageFrom } from '../shared/utils/format';
import { ClientDrift, diffClient } from '../shared/utils/drift';
import { ServersService } from '../api/api/servers.service';
import { ServerClientsService } from '../api/api/serverClients.service';
import { ServerModsService } from '../api/api/serverMods.service';
import { ClientDto } from '../api/model/clientDto';
import { ModDto } from '../api/model/modDto';
import { ServerDto } from '../api/model/serverDto';
import { ClientModsDialogService } from './client-mods-dialog';
import { serverIdSignal } from './server-route';

const POLL_MS = 10000;

@Component({
  selector: 'app-server-clients',
  imports: [
    ContentHeader,
    DatePipe,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmInputImports,
    HlmTableImports,
  ],
  providers: [provideIcons({ lucideRefreshCw, lucideSearch, lucideUsers })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">{{ serverName() }}</span>
    </app-content-header>

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">
          Clients
          <span class="text-muted-foreground font-normal">{{ summary() }}</span>
        </h2>
        <div class="flex items-center gap-2">
          <div class="relative">
            <ng-icon
              name="lucideSearch"
              size="14"
              class="text-muted-foreground absolute left-2 top-1/2 -translate-y-1/2"
            />
            <input
              hlmInput
              placeholder="Filter…"
              class="h-8 w-56 pl-7 text-xs"
              [value]="filter()"
              (input)="onFilter($event)"
            />
          </div>
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
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto px-4">
        @if (filteredRows().length === 0 && !loading()) {
          @if (rows().length === 0) {
            <div
              class="text-muted-foreground flex h-full flex-col items-center justify-center gap-2 p-10 text-center text-sm"
            >
              <ng-icon name="lucideUsers" size="28" class="opacity-60" />
              <p>No client has reported in to this server yet.</p>
              <p class="max-w-md text-xs">
                A client appears here the first time the locator finishes a sync. See
                <strong>Setup</strong> for the jar a player needs - it already carries this server's
                token, so a client that never shows up here is holding another server's jar or none
                at all.
              </p>
            </div>
          } @else {
            <p class="text-muted-foreground p-4 text-sm">No clients match "{{ filter() }}".</p>
          }
        } @else {
          <table hlmTable>
            <thead hlmTableHeader>
              <tr hlmTableRow>
                <th hlmTableHead>Username</th>
                <th hlmTableHead>Client ID</th>
                <th hlmTableHead>Last seen</th>
                <th hlmTableHead class="text-right">Mods</th>
                <th hlmTableHead>Diff</th>
                <th hlmTableHead class="text-right">Status</th>
              </tr>
            </thead>
            <tbody hlmTableBody>
              @for (row of filteredRows(); track row.client.id) {
                <tr hlmTableRow class="cursor-pointer" (click)="openDetails(row)">
                  <td hlmTableCell class="font-medium">
                    @if (row.client.username) {
                      {{ row.client.username }}
                    } @else {
                      <span class="text-muted-foreground italic">no username</span>
                    }
                  </td>
                  <td hlmTableCell class="font-mono text-xs" [title]="row.client.clientId">
                    {{ short(row.client.clientId) }}
                  </td>
                  <td
                    hlmTableCell
                    class="text-xs"
                    [title]="row.client.lastSeenAt | date: 'yyyy-MM-dd HH:mm:ss'"
                  >
                    {{ age(row.client.lastSeenAt) }}
                  </td>
                  <td hlmTableCell class="text-right font-mono text-xs">
                    {{ row.client.mods.length }}/{{ requiredCount() }}
                  </td>
                  <td hlmTableCell>
                    <span class="flex flex-wrap items-center gap-1">
                      @if (row.missing.length > 0) {
                        <span hlmBadge variant="outline" class="text-xs">
                          {{ row.missing.length }} missing
                        </span>
                      }
                      @if (row.unknown > 0) {
                        <span hlmBadge variant="destructive" class="text-xs">
                          {{ row.unknown }} unknown
                        </span>
                      }
                      @if (row.missing.length === 0 && row.unknown === 0) {
                        <span class="text-muted-foreground text-xs">-</span>
                      }
                    </span>
                  </td>
                  <td hlmTableCell class="text-right">
                    @switch (row.status) {
                      @case ('in sync') {
                        <span hlmBadge variant="default" class="text-xs">in sync</span>
                      }
                      @case ('drift') {
                        <span hlmBadge variant="destructive" class="text-xs">drift</span>
                      }
                      @default {
                        <span hlmBadge variant="secondary" class="text-xs">offline</span>
                      }
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </section>
  `,
})
export class ServerClients {
  private readonly route = inject(ActivatedRoute);
  private readonly serversApi = inject(ServersService);
  private readonly clientsApi = inject(ServerClientsService);
  private readonly modsApi = inject(ServerModsService);
  private readonly detailsDialog = inject(ClientModsDialogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly serverId = serverIdSignal(this.route);

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly clients = signal<ReadonlyArray<ClientDto>>([]);
  protected readonly requiredMods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly loading = signal(false);
  protected readonly filter = signal('');

  private readonly now = signal(Date.now());

  protected readonly serverName = computed(() => this.server()?.name ?? '');
  protected readonly requiredCount = computed(() => this.requiredMods().length);

  protected readonly rows = computed<ReadonlyArray<ClientDrift>>(() => {
    const required = this.requiredMods();
    const now = this.now();
    return this.clients().map((client) => diffClient(client, required, now));
  });

  protected readonly filteredRows = computed(() => {
    const q = this.filter().trim().toLowerCase();
    if (q === '') return this.rows();
    return this.rows().filter(
      (r) =>
        (r.client.username ?? '').toLowerCase().includes(q) ||
        r.client.clientId.toLowerCase().includes(q),
    );
  });

  protected readonly summary = computed(() => {
    const list = this.rows();
    if (list.length === 0) return '';
    const drifting = list.filter((r) => r.status === 'drift').length;
    return drifting === 0
      ? `· ${list.length} known`
      : `· ${list.length} known · ${drifting} drifting`;
  });

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id === '') return;
      this.serversApi.apiServersIdGet(id).subscribe({
        next: (server) => this.server.set(server),
        error: (err) => toast.error(messageFrom(err, 'Failed to load the server')),
      });
      this.load(id, false);
    });

    interval(POLL_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const id = this.serverId();
        if (id !== '') this.load(id, true);
      });
  }

  protected short(value: string): string {
    return value.slice(0, 12);
  }

  protected age(iso: string): string {
    return formatAge(iso, this.now());
  }

  protected onFilter(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }

  protected reload(): void {
    const id = this.serverId();
    if (id !== '') this.load(id, false);
  }

  private load(id: string, silent: boolean): void {
    if (!silent) this.loading.set(true);

    forkJoin({
      clients: this.clientsApi.apiServersIdClientsGet(id),
      mods: this.modsApi.apiServersIdModsGet(id),
    }).subscribe({
      next: (result) => {
        this.clients.set(result.clients);
        this.requiredMods.set(result.mods);
        this.now.set(Date.now());
        this.loading.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load clients'));
        this.loading.set(false);
      },
    });
  }

  protected openDetails(row: ClientDrift): void {
    this.detailsDialog.open({ client: row.client, missing: row.missing });
  }
}
