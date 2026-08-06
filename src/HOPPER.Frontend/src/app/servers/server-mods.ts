import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import {
  lucideClipboardList,
  lucideImport,
  lucidePackage,
  lucidePlus,
  lucideRefreshCw,
  lucideSearch,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { formatBytes, messageFrom, toNumber } from '../shared/utils/format';
import { ServersService } from '../api/api/servers.service';
import { ServerModsService } from '../api/api/serverMods.service';
import { ServerImportsService } from '../api/api/serverImports.service';
import { ModDto } from '../api/model/modDto';
import { ServerDto } from '../api/model/serverDto';
import { serverIdSignal } from './server-route';
import { ImportPackDialogService } from './import-pack-dialog';
import { UploadModsDialogService } from './upload-mods-dialog';

@Component({
  selector: 'app-server-mods',
  imports: [
    ContentHeader,
    CopyButton,
    DatePipe,
    NgIcon,
    RouterLink,
    HlmButtonImports,
    HlmInputImports,
    HlmTableImports,
  ],
  providers: [
    provideIcons({
      lucideClipboardList,
      lucideImport,
      lucidePackage,
      lucidePlus,
      lucideRefreshCw,
      lucideSearch,
      lucideTrash2,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">{{ serverName() }}</span>
    </app-content-header>

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">
          Mods
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
          <!-- Only when there is something to do. A pack import can leave jars only a human can
               fetch, and this is the count of them plus the way to the page that lists them - the
               mods table itself has no row for a file that was never stored. -->
          @if (pendingCount() > 0) {
            <a hlmBtn variant="outline" size="sm" [routerLink]="pendingLink()">
              <ng-icon name="lucideClipboardList" size="14" />
              Fetch by hand · {{ pendingCount() }}
            </a>
          }
          <button hlmBtn variant="outline" size="sm" type="button" (click)="importPack()">
            <ng-icon name="lucideImport" size="14" />
            Import pack
          </button>
          <button hlmBtn size="sm" type="button" (click)="upload()">
            <ng-icon name="lucidePlus" size="14" />
            Upload jars
          </button>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto px-4">
        @if (filteredMods().length === 0 && !loading()) {
          @if (mods().length === 0) {
            <div
              class="text-muted-foreground flex h-full flex-col items-center justify-center gap-2 p-10 text-center text-sm"
            >
              <ng-icon name="lucidePackage" size="28" class="opacity-60" />
              <p>No mods on this server yet.</p>
              <p class="max-w-md text-xs">
                Press <strong>Upload jars</strong> to add some. This server's manifest is served
                from this list, so anything here lands in the hopper folder of every player holding
                its jar - and anything not here is deleted from theirs.
              </p>
            </div>
          } @else {
            <p class="text-muted-foreground p-4 text-sm">No mods match "{{ filter() }}".</p>
          }
        } @else {
          <table hlmTable>
            <thead hlmTableHeader>
              <tr hlmTableRow>
                <th hlmTableHead>File</th>
                <th hlmTableHead>SHA-256</th>
                <th hlmTableHead class="text-right">Size</th>
                <th hlmTableHead>Uploaded by</th>
                <th hlmTableHead>Added</th>
                <th hlmTableHead class="text-right">Actions</th>
              </tr>
            </thead>
            <tbody hlmTableBody>
              @for (m of filteredMods(); track m.id) {
                <tr hlmTableRow>
                  <td hlmTableCell class="font-medium">{{ m.fileName }}</td>
                  <td hlmTableCell>
                    <span class="inline-flex items-center gap-1">
                      <span class="font-mono text-xs" [title]="m.sha256">{{ short(m.sha256) }}</span>
                      <app-copy-button [value]="m.sha256" />
                    </span>
                  </td>
                  <td hlmTableCell class="text-right font-mono text-xs">{{ size(m) }}</td>
                  <td hlmTableCell class="text-sm">
                    @if (m.uploadedBy) {
                      {{ m.uploadedBy }}
                    } @else {
                      <span class="text-muted-foreground italic">unknown</span>
                    }
                  </td>
                  <td hlmTableCell class="font-mono text-xs">
                    {{ m.createdAt | date: 'yyyy-MM-dd HH:mm' }}
                  </td>
                  <td hlmTableCell class="text-right">
                    <button
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      type="button"
                      title="Remove mod"
                      [disabled]="deleting()[m.id]"
                      (click)="remove(m)"
                    >
                      <ng-icon name="lucideTrash2" size="14" />
                    </button>
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
export class ServerMods {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ServerModsService);
  private readonly serversApi = inject(ServersService);
  private readonly importsApi = inject(ServerImportsService);
  private readonly router = inject(Router);
  private readonly uploadDialog = inject(UploadModsDialogService);
  private readonly importDialog = inject(ImportPackDialogService);
  private readonly confirm = inject(ConfirmService);

  protected readonly serverId = serverIdSignal(this.route);

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly mods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly loading = signal(false);
  protected readonly filter = signal('');
  protected readonly deleting = signal<Record<string, boolean>>({});
  protected readonly pendingCount = signal(0);

  protected readonly serverName = computed(() => this.server()?.name ?? '');

  // Matching the full hash as well as the filename means a hash pasted from a client's report or
  // from a blob URL finds its row.
  protected readonly filteredMods = computed(() => {
    const q = this.filter().trim().toLowerCase();
    if (q === '') return this.mods();
    return this.mods().filter(
      (m) => m.fileName.toLowerCase().includes(q) || m.sha256.toLowerCase().includes(q),
    );
  });

  protected readonly summary = computed(() => {
    const list = this.mods();
    if (list.length === 0) return '';
    const total = list.reduce((sum, m) => sum + toNumber(m.size), 0);
    return `· ${list.length} jar${list.length === 1 ? '' : 's'} · ${formatBytes(total)}`;
  });

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id !== '') this.load(id);
    });
  }

  protected pendingLink(): ReadonlyArray<string> {
    return ['/server', this.serverId(), 'pending'];
  }

  protected short(sha256: string): string {
    return sha256.slice(0, 12);
  }

  protected size(mod: ModDto): string {
    return formatBytes(toNumber(mod.size));
  }

  protected onFilter(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }

  protected reload(): void {
    const id = this.serverId();
    if (id !== '') this.load(id);
  }

  private load(id: string): void {
    this.loading.set(true);
    // The pending list rides along with the mods: it is the other half of what this server owes its
    // clients, and the two are read together or the badge lags a jar behind.
    forkJoin({
      server: this.serversApi.apiServersIdGet(id),
      mods: this.api.apiServersIdModsGet(id),
      pending: this.importsApi.apiServersIdPendingGet(id),
    }).subscribe({
      next: (result) => {
        this.server.set(result.server);
        this.mods.set(result.mods);
        this.pendingCount.set(result.pending.length);
        this.loading.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load mods'));
        this.loading.set(false);
      },
    });
  }

  protected async upload(): Promise<void> {
    const id = this.serverId();
    if (id === '') return;
    // The dialog raises its own toasts for the per-file outcome, so the page only has to refresh:
    // even a batch that partly failed changed the list.
    const result = await this.uploadDialog.open({ serverId: id });
    if (result) this.reload();
  }

  protected async importPack(): Promise<void> {
    const id = this.serverId();
    if (id === '') return;

    const result = await this.importDialog.open({ serverId: id });
    if (!result) return;
    this.reload();

    // The pending list is the import's unfinished half, and it is work for later: the admin has to
    // go and download jars a machine is not allowed to. The dialog's exit hands them the page that
    // keeps the list rather than closing over it.
    if (result.openPending) await this.router.navigate(this.pendingLink() as string[]);
  }

  protected async remove(mod: ModDto): Promise<void> {
    const id = this.serverId();
    if (id === '') return;

    const ok = await this.confirm.open({
      title: `Remove ${mod.fileName}?`,
      message:
        "It leaves this server's manifest immediately, and its clients delete their copy on the next launch. Another server carrying the same jar keeps it.",
      confirmLabel: 'Remove',
      destructive: true,
    });
    if (!ok) return;

    this.deleting.update((d) => ({ ...d, [mod.id]: true }));
    this.api.apiServersIdModsModIdDelete(id, mod.id).subscribe({
      next: () => {
        this.deleting.update((d) => ({ ...d, [mod.id]: false }));
        this.reload();
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to remove the mod'));
        this.deleting.update((d) => ({ ...d, [mod.id]: false }));
      },
    });
  }
}
