import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
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
  lucideShare,
  lucideTrash2,
} from '@ng-icons/lucide';
import { simpleModrinth } from '@ng-icons/simple-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
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
import { WhenPipe } from '../shared/utils/when';
import { ServerChanged } from '../shared/services/server-changed';
import { serverIdSignal } from './server-route';
import { modIconUrl, modSideLabel, modSourceLabel, modrinthProjectUrl } from './mod-labels';
import { BASE_PATH } from '../api/variables';
import { ModSide } from '../api/model/modSide';
import { ExportPackDialogService } from './export-pack-dialog';
import { ImportPackDialogService } from './import-pack-dialog';
import { UploadModsDialogService } from './upload-mods-dialog';

@Component({
  selector: 'app-server-mods',
  imports: [
    ContentHeader,
    WhenPipe,
    CopyButton,
    NgIcon,
    RouterLink,
    HlmButtonImports,
    ButtonLoading,
    HlmInputImports,
    HlmTableImports,
    HlmBadgeImports,
  ],
  providers: [
    provideIcons({
      simpleModrinth,
      lucideClipboardList,
      lucideImport,
      lucidePackage,
      lucidePlus,
      lucideRefreshCw,
      lucideSearch,
      lucideShare,
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
          <!-- Only when there is something to do. A pack import can leave jars only a human can
               fetch, and this is the count of them plus the way to the page that lists them - the
               mods table itself has no row for a file that was never stored. -->
          @if (pendingCount() > 0) {
            <a hlmBtn variant="outline" size="sm" [routerLink]="pendingLink()">
              <ng-icon name="lucideClipboardList" size="14" />
              Fetch by hand · {{ pendingCount() }}
            </a>
          }
          <a hlmBtn variant="outline" size="sm" [routerLink]="browseLink()">
            <ng-icon name="simpleModrinth" size="14" />
            Browse Modrinth
          </a>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="importPack()">
            <ng-icon name="lucideImport" size="14" />
            Import pack
          </button>
          <!-- The other direction. Disabled while the list is loading rather than hidden: the
               dialog quotes a size per format from exactly this array, and quoting it from a
               half-loaded one would understate the download. -->
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            [disabled]="loading()"
            (click)="exportPack()"
          >
            <ng-icon name="lucideShare" size="14" />
            Export pack
          </button>
          <button hlmBtn size="sm" type="button" (click)="upload()">
            <ng-icon name="lucidePlus" size="14" />
            Upload jars
          </button>
        </div>
      </header>

      <!-- Its own strip rather than more buttons in the header: the header is already five
           controls wide and wraps on a narrow window, and this appears and disappears. -->
      @if (selectedCount() > 0) {
        <div class="bg-muted/40 flex flex-wrap items-center gap-2 border-b px-4 py-2">
          <span class="text-sm font-medium">{{ selectedCount() }} selected</span>
          <span class="text-muted-foreground text-xs">Set side to</span>
          @for (choice of sideChoices; track choice.side) {
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="settingSide()"
              (click)="setSide(choice.side)"
            >
              {{ choice.label }}
            </button>
          }
          <button hlmBtn variant="ghost" size="sm" type="button" (click)="clearSelection()">
            Clear
          </button>
        </div>
      }

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
                <th hlmTableHead class="w-8">
                  <input
                    type="checkbox"
                    class="size-3.5 align-middle"
                    aria-label="Select every mod shown"
                    [checked]="allShownSelected()"
                    [indeterminate]="someShownSelected()"
                    (change)="toggleAllShown()"
                  />
                </th>
                <th hlmTableHead class="w-10"><span class="sr-only">Icon</span></th>
                <th hlmTableHead>File</th>
                <th hlmTableHead>Side</th>
                <th hlmTableHead>Source</th>
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
                  <td hlmTableCell>
                    <input
                      type="checkbox"
                      class="size-3.5 align-middle"
                      [attr.aria-label]="'Select ' + m.fileName"
                      [checked]="isSelected(m.id)"
                      (change)="toggle(m.id)"
                    />
                  </td>
                  <td hlmTableCell>
                    <!-- The placeholder keeps the column from collapsing on a server whose jars
                         carry no icon, which is every hand-built pack until the backfill runs. -->
                    <!-- The jar's own icon first, because it is served by HOPPER and works with no
                         network. The platform's URL is the fallback for what the manager installed,
                         where the jar usually carries none. -->
                    @if (iconOf(m); as icon) {
                      <img
                        [src]="icon"
                        [alt]="m.fileName"
                        loading="lazy"
                        class="size-7 rounded object-cover"
                        (error)="onIconError(m.id)"
                      />
                    } @else {
                      <span
                        class="bg-muted text-muted-foreground flex size-7 items-center justify-center rounded text-xs"
                        aria-hidden="true"
                        >?</span
                      >
                    }
                  </td>
                  <td hlmTableCell class="font-medium">{{ m.fileName }}</td>
                  <!-- Quiet for Both, which is nearly every row. Only the exceptions should draw
                       the eye, because those are the ones somebody decided. -->
                  <td hlmTableCell class="text-sm">
                    @if (m.side === ModSide.Both) {
                      <span class="text-muted-foreground text-xs">Both</span>
                    } @else {
                      <span hlmBadge variant="secondary" class="text-xs">{{ sideLabel(m.side) }}</span>
                    }
                  </td>
                  <!-- Where the jar came from, and where it goes in an exported pack: a mod with
                       Modrinth provenance becomes a manifest entry with its real CDN URL, and
                       anything else ships as bytes in overrides/. -->
                  <td hlmTableCell class="text-sm">
                    <span class="flex flex-col">
                      <span>{{ source(m) }}</span>
                      @if (m.projectName) {
                        @if (projectUrl(m); as url) {
                          <a
                            class="text-muted-foreground truncate text-xs hover:underline"
                            [href]="url"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            {{ m.projectName }}
                          </a>
                        } @else {
                          <span class="text-muted-foreground truncate text-xs">
                            {{ m.projectName }}
                          </span>
                        }
                      }
                    </span>
                  </td>
                  <td hlmTableCell>
                    <span class="inline-flex items-center gap-1">
                      <span class="font-mono text-xs" [title]="m.sha256">{{
                        short(m.sha256)
                      }}</span>
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
                    {{ m.createdAt | when }}
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
  private readonly apiBaseUrl = inject(BASE_PATH, { optional: true }) ?? '';
  private readonly serverChanged = inject(ServerChanged);
  private readonly api = inject(ServerModsService);
  private readonly serversApi = inject(ServersService);
  private readonly importsApi = inject(ServerImportsService);
  private readonly router = inject(Router);
  private readonly uploadDialog = inject(UploadModsDialogService);
  private readonly importDialog = inject(ImportPackDialogService);
  private readonly exportDialog = inject(ExportPackDialogService);
  private readonly confirm = inject(ConfirmService);

  protected readonly serverId = serverIdSignal(this.route);

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly mods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly loading = signal(false);
  protected readonly filter = signal('');
  protected readonly deleting = signal<Record<string, boolean>>({});
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly settingSide = signal(false);

  protected readonly ModSide = ModSide;
  protected readonly sideLabel = modSideLabel;

  protected readonly sideChoices = [
    { side: ModSide.Both, label: 'Both' },
    { side: ModSide.ClientOnly, label: 'Client only' },
    { side: ModSide.ServerOnly, label: 'Server only' },
  ] as const;
  protected readonly pendingCount = signal(0);

  protected readonly serverName = computed(() => this.server()?.name ?? '');

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
    const base = `· ${list.length} jar${list.length === 1 ? '' : 's'} · ${formatBytes(total)}`;

    const clients = list.filter((m) => m.side !== ModSide.ServerOnly).length;
    const servers = list.filter((m) => m.side !== ModSide.ClientOnly).length;
    if (clients === list.length && servers === list.length) return base;

    return `${base} · ${clients} to clients · ${servers} to the server`;
  });

  protected readonly selectedCount = computed(() => this.selected().size);

  protected readonly allShownSelected = computed(() => {
    const shown = this.filteredMods();
    return shown.length > 0 && shown.every((m) => this.selected().has(m.id));
  });

  protected readonly someShownSelected = computed(() => {
    const shown = this.filteredMods();
    const picked = shown.filter((m) => this.selected().has(m.id)).length;
    return picked > 0 && picked < shown.length;
  });

  protected isSelected(id: string): boolean {
    return this.selected().has(id);
  }

  protected toggle(id: string): void {
    const next = new Set(this.selected());
    if (!next.delete(id)) next.add(id);
    this.selected.set(next);
  }

  protected toggleAllShown(): void {
    const shown = this.filteredMods();
    const next = new Set(this.selected());
    if (this.allShownSelected()) {
      shown.forEach((m) => next.delete(m.id));
    } else {
      shown.forEach((m) => next.add(m.id));
    }
    this.selected.set(next);
  }

  protected clearSelection(): void {
    this.selected.set(new Set());
  }

  protected setSide(side: ModSide): void {
    const ids = [...this.selected()];
    if (ids.length === 0) return;

    this.settingSide.set(true);
    this.api
      .apiServersIdModsSidePatch(this.serverId(), { modIds: ids, side })
      .subscribe({
        next: (result) => {
          this.settingSide.set(false);
          this.clearSelection();
          toast.success(
            `${result.updated} mod${result.updated === 1 ? '' : 's'} set to ${modSideLabel(side)}.`,
          );
          this.reload();
        },
        error: () => {
          this.settingSide.set(false);
          toast.error('Could not change the side.');
        },
      });
  }

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id !== '') this.load(id);
    });
  }

  protected pendingLink(): ReadonlyArray<string> {
    return ['/server', this.serverId(), 'pending'];
  }

  protected browseLink(): ReadonlyArray<string> {
    return ['/server', this.serverId(), 'browse'];
  }

  protected source(mod: ModDto): string {
    return modSourceLabel(mod.source);
  }

  protected projectUrl(mod: ModDto): string | null {
    return modrinthProjectUrl(mod.projectId);
  }

  protected short(sha256: string): string {
    return sha256.slice(0, 12);
  }

  protected size(mod: ModDto): string {
    return formatBytes(toNumber(mod.size));
  }

  protected readonly iconFailed = signal<Record<string, boolean>>({});

  protected iconOf(mod: ModDto): string | null {
    if (this.iconFailed()[mod.id]) return null;

    return modIconUrl(mod, this.apiBaseUrl);
  }

  protected onIconError(modId: string): void {
    this.iconFailed.update((failed) => ({ ...failed, [modId]: true }));
  }

  protected onFilter(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }

  protected reload(): void {
    const id = this.serverId();
    if (id === '') return;
    this.load(id);
    this.serverChanged.changed();
  }

  private load(id: string): void {
    this.loading.set(true);

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

    const result = await this.uploadDialog.open({ serverId: id });
    if (result) this.reload();
  }

  protected async importPack(): Promise<void> {
    const id = this.serverId();
    if (id === '') return;

    const result = await this.importDialog.open({ serverId: id });
    if (!result) return;
    this.reload();

    if (result.openPending) await this.router.navigate(this.pendingLink() as string[]);
  }

  protected async exportPack(): Promise<void> {
    const server = this.server();
    if (!server) return;

    const result = await this.exportDialog.open({ server, mods: this.mods() });
    if (result) toast.success(`Downloaded ${result.fileName}`);
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
