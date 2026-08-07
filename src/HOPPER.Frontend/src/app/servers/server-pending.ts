import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import { lucideCircleCheck, lucideRefreshCw } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { messageFrom } from '../shared/utils/format';
import { ServersService } from '../api/api/servers.service';
import { ServerImportsService } from '../api/api/serverImports.service';
import { ModImportDto } from '../api/model/modImportDto';
import { PendingModDto } from '../api/model/pendingModDto';
import { PackFormat } from '../api/model/packFormat';
import { ServerDto } from '../api/model/serverDto';
import { WhenPipe } from '../shared/utils/when';
import { serverIdSignal } from './server-route';
import { packFormatLabel } from './import-labels';
import { groupPendingByImport } from './pending-groups';
import { PendingMods } from './pending-mods';

@Component({
  selector: 'app-server-pending',
  imports: [ContentHeader, WhenPipe, NgIcon, HlmButtonImports,
    ButtonLoading, PendingMods],
  providers: [provideIcons({ lucideCircleCheck, lucideRefreshCw })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">{{ serverName() }}</span>
    </app-content-header>

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">
          Fetch by hand
          <span class="text-muted-foreground font-normal">{{ summary() }}</span>
        </h2>
        <button
          [loading]="loading()"
          hlmBtn
          variant="outline"
          size="sm"
          type="button"
          (click)="reload()"
          [disabled]="loading()"
        >
          <ng-icon name="lucideRefreshCw" size="14" />
          {{ loading() ? 'Loading' : 'Refresh' }}
        </button>
      </header>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        @if (groups().length === 0) {
          @if (!loading()) {
            <div
              class="text-muted-foreground flex h-full flex-col items-center justify-center gap-2 p-10 text-center text-sm"
            >
              <ng-icon name="lucideCircleCheck" size="28" class="opacity-60" />
              <p>Nothing is waiting on you.</p>
              <p class="max-w-md text-xs">
                An import lists a file here when it cannot fetch it itself - a CurseForge entry with
                no API key to resolve it, a mod whose author blocked third-party downloads, or a
                download that failed. Whatever lands here is a jar you download yourself and hand
                back with <strong>Supply jar</strong>.
              </p>
            </div>
          }
        } @else {
          <div class="flex flex-col gap-4">
            <p class="text-muted-foreground text-xs">
              Open each project page, download the file, then hand it back with
              <strong>Supply jar</strong> - it is checked against the hash the pack declared, where
              there is one, and stored under the filename the pack expects. Drop the ones you have
              decided not to carry.
            </p>

            @for (group of groups(); track group.importId) {
              <section class="flex flex-col gap-2">
                <div class="flex flex-wrap items-baseline justify-between gap-2">
                  <h3 class="truncate text-sm font-medium" [title]="group.sourceName">
                    {{ group.sourceName }}
                  </h3>
                  <span class="text-muted-foreground text-xs">
                    {{ format(group.format) }}
                    @if (group.createdAt; as when) {
                      · {{ when | when }}
                    }
                    · {{ group.entries.length }} open
                  </span>
                </div>

                <app-pending-mods
                  [serverId]="serverId()"
                  [entries]="group.entries"
                  (resolved)="reload()"
                  (dismissed)="drop($event)"
                />
              </section>
            }
          </div>
        }
      </div>
    </section>
  `,
})
export class ServerPending {
  private readonly route = inject(ActivatedRoute);
  private readonly serversApi = inject(ServersService);
  private readonly importsApi = inject(ServerImportsService);

  protected readonly serverId = serverIdSignal(this.route);

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly pending = signal<ReadonlyArray<PendingModDto>>([]);
  protected readonly imports = signal<ReadonlyArray<ModImportDto>>([]);
  protected readonly loading = signal(false);

  protected readonly serverName = computed(() => this.server()?.name ?? '');

  protected readonly groups = computed(() => groupPendingByImport(this.pending(), this.imports()));

  protected readonly summary = computed(() => {
    const open = this.pending().length;
    if (open === 0) return '';
    return `· ${open} file${open === 1 ? '' : 's'} to collect`;
  });

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id !== '') this.load(id);
    });
  }

  protected format(value: PackFormat): string {
    return packFormatLabel(value);
  }

  protected reload(): void {
    const id = this.serverId();
    if (id !== '') this.load(id);
  }

  protected drop(entry: PendingModDto): void {
    this.pending.update((list) => list.filter((p) => p.id !== entry.id));
  }

  private load(id: string): void {
    this.loading.set(true);

    forkJoin({
      server: this.serversApi.apiServersIdGet(id),
      pending: this.importsApi.apiServersIdPendingGet(id),
      imports: this.importsApi.apiServersIdImportsGet(id),
    }).subscribe({
      next: (result) => {
        this.server.set(result.server);
        this.pending.set(result.pending);
        this.imports.set(result.imports);
        this.loading.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load the pending list'));
        this.loading.set(false);
      },
    });
  }
}
