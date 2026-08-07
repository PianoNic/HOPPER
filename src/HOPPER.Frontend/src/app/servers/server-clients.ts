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
import { ActivatedRoute } from '@angular/router';
import {
  catchError,
  EMPTY,
  filter as rxFilter,
  forkJoin,
  fromEvent,
  interval,
  Observable,
  switchMap,
} from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import { lucideRefreshCw, lucideSearch, lucideServer, lucideUsers } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { formatAge, messageFrom } from '../shared/utils/format';
import { ClientDrift, diffClient, reaches } from '../shared/utils/drift';
import { SYNC_SIDE } from './mod-labels';
import { ServersService } from '../api/api/servers.service';
import { ServerClientsService } from '../api/api/serverClients.service';
import { ServerModsService } from '../api/api/serverMods.service';
import { ClientDto } from '../api/model/clientDto';
import { ModDto } from '../api/model/modDto';
import { ServerDto } from '../api/model/serverDto';
import { ClientModsDialogService } from './client-mods-dialog';
import { shouldPoll } from './poll-gate';
import { WhenPipe } from '../shared/utils/when';
import { serverIdSignal } from './server-route';

type ClientsSnapshot = {
  clients: ReadonlyArray<ClientDto>;
  mods: ReadonlyArray<ModDto>;
};

const POLL_MS = 10000;

@Component({
  selector: 'app-server-clients',
  imports: [
    ContentHeader,
    WhenPipe,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    ButtonLoading,
    HlmInputImports,
    HlmTableImports,
  ],
  providers: [provideIcons({ lucideRefreshCw, lucideSearch, lucideServer, lucideUsers })],
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
            [loading]="loading()"
          >
            <ng-icon name="lucideRefreshCw" size="14" />
            {{ refreshLabel() }}
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
                  <!-- A dedicated server has no username and never will, so the side is its
                       identity rather than a blank where a name should be. -->
                  <td hlmTableCell class="font-medium">
                    @if (isServer(row)) {
                      <span class="inline-flex items-center gap-1.5">
                        <ng-icon name="lucideServer" size="14" class="text-muted-foreground" />
                        Dedicated server
                      </span>
                    } @else if (row.client.username) {
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
                    [title]="row.client.lastSeenAt | when: 'long'"
                  >
                    {{ age(row.client.lastSeenAt) }}
                  </td>
                  <td hlmTableCell class="text-right font-mono text-xs">
                    {{ matchedFor(row) }}/{{ requiredFor(row) }}
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

  private readonly pollFailed = signal(false);

  private readonly now = signal(Date.now());

  protected readonly serverName = computed(() => this.server()?.name ?? '');

  protected readonly rows = computed<ReadonlyArray<ClientDrift>>(() => {
    const required = this.requiredMods();
    const now = this.now();
    const rows = this.clients().map((client) => diffClient(client, required, now));

    return [...rows].sort((a, b) => Number(this.isServer(b)) - Number(this.isServer(a)));
  });

  protected isServer(row: ClientDrift): boolean {
    return row.client.side === SYNC_SIDE.server;
  }

  protected requiredFor(row: ClientDrift): number {
    return this.requiredMods().filter((m) => reaches(m, row.client.side)).length;
  }

  protected matchedFor(row: ClientDrift): number {
    return this.requiredFor(row) - row.missing.length;
  }

  protected readonly filteredRows = computed(() => {
    const q = this.filter().trim().toLowerCase();
    if (q === '') return this.rows();
    return this.rows().filter(
      (r) =>
        (r.client.username ?? '').toLowerCase().includes(q) ||
        r.client.clientId.toLowerCase().includes(q) ||
        (this.isServer(r) && 'dedicated server'.includes(q)),
    );
  });

  protected readonly refreshLabel = computed(() => {
    if (this.loading()) return 'Loading';
    return this.pollFailed() ? 'Reconnect' : 'Refresh';
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
      this.load(id);
    });

    fromEvent(document, 'visibilitychange')
      .pipe(
        rxFilter(() => shouldPoll({
          hidden: document.hidden,
          hasServer: this.serverId() !== '',
          failed: this.pollFailed(),
        })),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.load(this.serverId()));

    interval(POLL_MS)
      .pipe(
        rxFilter(() =>
          shouldPoll({
            hidden: document.hidden,
            hasServer: this.serverId() !== '',
            failed: this.pollFailed(),
          }),
        ),
        switchMap(() =>
          this.request(this.serverId()).pipe(
            catchError((err: unknown) => {
              toast.error(
                messageFrom(err, 'Lost contact with the server - live updates are paused'),
              );
              this.pollFailed.set(true);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snapshot) => this.apply(snapshot));
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
    if (id !== '') this.load(id);
  }

  private request(id: string): Observable<ClientsSnapshot> {
    return forkJoin({
      clients: this.clientsApi.apiServersIdClientsGet(id),
      mods: this.modsApi.apiServersIdModsGet(id),
    });
  }

  private apply(snapshot: ClientsSnapshot): void {
    this.clients.set(snapshot.clients);
    this.requiredMods.set(snapshot.mods);
    this.now.set(Date.now());

    this.pollFailed.set(false);
    this.loading.set(false);
  }

  private load(id: string): void {
    this.loading.set(true);

    this.request(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (snapshot) => this.apply(snapshot),
        error: (err: unknown) => {
          toast.error(messageFrom(err, 'Failed to load clients'));
          this.pollFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected openDetails(row: ClientDrift): void {
    this.detailsDialog.open({ client: row.client, missing: row.missing });
  }
}
