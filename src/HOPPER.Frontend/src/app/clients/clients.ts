import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
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
import { ClientsService } from '../api/api/clients.service';
import { ModsService } from '../api/api/mods.service';
import { ClientDto } from '../api/model/clientDto';
import { ModDto } from '../api/model/modDto';
import { ClientModsDialogService } from './client-mods-dialog';

const POLL_MS = 10000;

@Component({
  selector: 'app-clients',
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
    <app-content-header />

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
              (input)="filter.set($any($event.target).value)"
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
              class="text-muted-foreground flex flex-col items-center gap-2 p-10 text-center text-sm"
            >
              <ng-icon name="lucideUsers" size="28" class="opacity-60" />
              <p>No client has reported in yet.</p>
              <p class="max-w-md text-xs">
                A client appears here the first time the locator finishes a sync. See
                <strong>Setup</strong> for the hopper.properties a player needs.
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
                        <span class="text-muted-foreground text-xs">—</span>
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
export class Clients {
  private readonly clientsApi = inject(ClientsService);
  private readonly modsApi = inject(ModsService);
  private readonly detailsDialog = inject(ClientModsDialogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly clients = signal<ReadonlyArray<ClientDto>>([]);
  protected readonly requiredMods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly loading = signal(false);
  protected readonly filter = signal('');

  // The clock the relative "last seen" labels read. Ticked with the poll so the labels age
  // visibly under OnPush instead of freezing at whatever they said on first render.
  private readonly now = signal(Date.now());

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
    this.reload();
    // Reports arrive whenever someone launches the game, so poll on a light cadence. Silent
    // reloads keep the Refresh button from flickering under the user.
    interval(POLL_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reload(true));
  }

  protected short(value: string): string {
    return value.slice(0, 12);
  }

  protected age(iso: string): string {
    return formatAge(iso, this.now());
  }

  protected reload(silent = false): void {
    if (!silent) this.loading.set(true);

    // Both halves of the diff have to come from the same moment, otherwise a mod uploaded between
    // the two calls shows up as "missing" on every client for one poll.
    forkJoin({
      clients: this.clientsApi.apiClientsGet(),
      mods: this.modsApi.apiModsGet(),
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
