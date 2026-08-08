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
  lucideTriangleAlert,
} from '@ng-icons/lucide';
import { simpleModrinth } from '@ng-icons/simple-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmContextMenuImports } from '@spartan-ng/helm/context-menu';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
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
import { SyncSide } from '../api/model/syncSide';
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
    HlmCheckboxImports,
    HlmContextMenuImports,
    HlmDropdownMenuImports,
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
      lucideTriangleAlert,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">{{ serverName() }}</span>
    </app-content-header>
    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">Mods</h2>
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
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            class="text-destructive hover:text-destructive ml-auto"
            [loading]="deletingSelection()"
            [disabled]="deletingSelection()"
            (click)="removeSelected()"
          >
            <ng-icon name="lucideTrash2" size="14" />
            Delete
          </button>
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
                  <hlm-checkbox
                    aria-label="Select every mod shown"
                    [checked]="allShownSelected()"
                    [indeterminate]="someShownSelected()"
                    (checkedChange)="toggleAllShown()"
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
                <tr
                  hlmTableRow
                  [hlmContextMenuTrigger]="sideMenu"
                  [hlmContextMenuTriggerData]="{ $implicit: m }"
                >
                  <td hlmTableCell>
                    <hlm-checkbox
                      [attr.aria-label]="'Select ' + m.fileName"
                      [checked]="isSelected(m.id)"
                      (checkedChange)="toggle(m.id)"
                    />
                  </td>
                  <td hlmTableCell>
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
                  <td hlmTableCell class="font-medium">
                    <span class="flex items-center gap-2">
                      {{ m.fileName }}
                      @if (m.collidesOn) {
                        <span
                          hlmBadge
                          variant="destructive"
                          class="text-xs"
                          [title]="collisionHint(m.collidesOn)"
                        >
                          <ng-icon name="lucideTriangleAlert" size="12" />
                          Duplicate mod id
                        </span>
                      }
                      @if (m.bytesMissing) {
                        <span
                          hlmBadge
                          variant="destructive"
                          class="text-xs"
                          title="HOPPER has the record but not the jar, so every client asking for it gets a 404. Install it again from Browse mods, or upload the file."
                        >
                          <ng-icon name="lucideTriangleAlert" size="12" />
                          Bytes missing
                        </span>
                      }
                    </span>
                  </td>
                  <td hlmTableCell class="text-sm">
                    @if (m.side === ModSide.Both) {
                      <span class="text-muted-foreground text-xs">Both</span>
                    } @else {
                      <span hlmBadge variant="secondary" class="text-xs">{{ sideLabel(m.side) }}</span>
                    }
                  </td>
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
                      [disabled]="deletingSelection()"
                      (click)="removeFor(m)"
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
    <ng-template #sideMenu let-mod>
      <div hlmDropdownMenu class="w-52">
        <div hlmDropdownMenuLabel>{{ contextLabel(mod) }}</div>
        <div hlmDropdownMenuSeparator></div>
        @for (choice of sideChoices; track choice.side) {
          <button
            hlmDropdownMenuItem
            type="button"
            [disabled]="settingSide()"
            (click)="setSideFor(mod, choice.side)"
          >
            {{ choice.label }}
          </button>
        }
        <div hlmDropdownMenuSeparator></div>
        <button
          hlmDropdownMenuItem
          type="button"
          class="text-destructive"
          [disabled]="deletingSelection()"
          (click)="removeFor(mod)"
        >
          <ng-icon name="lucideTrash2" size="14" />
          Delete
        </button>
      </div>
    </ng-template>
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

  protected readonly deletingSelection = signal(false);

  protected readonly serverId = serverIdSignal(this.route);

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly mods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly loading = signal(false);
  protected readonly filter = signal('');
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly settingSide = signal(false);

  protected readonly ModSide = ModSide;
  protected readonly sideLabel = modSideLabel;

  protected collisionHint(side: SyncSide): string {
    const who = side === SyncSide.Server ? 'the dedicated server' : 'a player';
    return `Another jar on this server declares the same mod id, and ${who} receives both. A loader refuses to start with two copies of one mod - set one of them to the opposite side, or remove it.`;
  }

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
    this.applySide([...this.selected()], side);
  }

  // Right-clicking a row the selection does not contain acts on that row alone. Right-clicking one
  // it does acts on the whole selection, which is what every table that offers both does.
  protected setSideFor(mod: ModDto, side: ModSide): void {
    const selection = this.selected();

    this.applySide(selection.has(mod.id) ? [...selection] : [mod.id], side);
  }

  protected contextLabel(mod: ModDto): string {
    const selection = this.selected();
    if (!selection.has(mod.id)) return mod.fileName;

    return `${selection.size} selected`;
  }

  private applySide(ids: ReadonlyArray<string>, side: ModSide): void {
    if (ids.length === 0) return;

    this.settingSide.set(true);
    this.api
      .apiServersIdModsSidePatch(this.serverId(), { modIds: [...ids], side })
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

  protected removeSelected(): void {
    void this.removeMany([...this.selected()]);
  }

  // Same rule as the side menu: a row inside the selection takes the whole selection with it.
  protected removeFor(mod: ModDto): void {
    const selection = this.selected();

    void this.removeMany(selection.has(mod.id) ? [...selection] : [mod.id]);
  }

  private async removeMany(ids: ReadonlyArray<string>): Promise<void> {
    const serverId = this.serverId();
    if (serverId === '' || ids.length === 0) return;

    const named =
      ids.length === 1
        ? (this.mods().find((m) => m.id === ids[0])?.fileName ?? 'this mod')
        : `${ids.length} mods`;

    const ok = await this.confirm.open({
      title: `Remove ${named}?`,
      message:
        "They leave this server's manifest immediately, and its clients delete their copies on the next launch. Another server carrying the same jar keeps it.",
      confirmLabel: 'Remove',
      destructive: true,
    });
    if (!ok) return;

    this.deletingSelection.set(true);
    this.api.apiServersIdModsDeletePost(serverId, { modIds: [...ids] }).subscribe({
      next: (result) => {
        this.deletingSelection.set(false);
        this.clearSelection();
        toast.success(`Removed ${result.deleted} mod${result.deleted === 1 ? '' : 's'}.`);
        this.reload();
      },
      error: (err) => {
        this.deletingSelection.set(false);
        toast.error(messageFrom(err, 'Failed to remove the mods'));
      },
    });
  }

}
