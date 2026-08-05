import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import { lucidePackage, lucidePlus, lucideRefreshCw, lucideSearch, lucideTrash2 } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { formatBytes, messageFrom, toNumber } from '../shared/utils/format';
import { ModsService } from '../api/api/mods.service';
import { ModDto } from '../api/model/modDto';
import { UploadModDialogService } from './upload-mod-dialog';

@Component({
  selector: 'app-mods',
  imports: [
    ContentHeader,
    CopyButton,
    DatePipe,
    NgIcon,
    HlmButtonImports,
    HlmInputImports,
    HlmTableImports,
  ],
  providers: [
    provideIcons({ lucidePackage, lucidePlus, lucideRefreshCw, lucideSearch, lucideTrash2 }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

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
          <button hlmBtn size="sm" type="button" (click)="upload()">
            <ng-icon name="lucidePlus" size="14" />
            Upload mod
          </button>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto px-4">
        @if (filteredMods().length === 0 && !loading()) {
          @if (mods().length === 0) {
            <div
              class="text-muted-foreground flex flex-col items-center gap-2 p-10 text-center text-sm"
            >
              <ng-icon name="lucidePackage" size="28" class="opacity-60" />
              <p>No mods yet.</p>
              <p class="max-w-md text-xs">
                Press <strong>Upload mod</strong> to add a jar. The manifest is served from this
                list, so anything here lands in every player's hopper folder — and anything not
                here is deleted from it.
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
export class Mods {
  private readonly api = inject(ModsService);
  private readonly uploadDialog = inject(UploadModDialogService);
  private readonly confirm = inject(ConfirmService);

  protected readonly mods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly loading = signal(false);
  protected readonly filter = signal('');
  protected readonly deleting = signal<Record<string, boolean>>({});

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
    this.reload();
  }

  protected short(sha256: string): string {
    return sha256.slice(0, 12);
  }

  protected size(mod: ModDto): string {
    return formatBytes(toNumber(mod.size));
  }

  protected reload(): void {
    this.loading.set(true);
    this.api.apiModsGet().subscribe({
      next: (mods) => {
        this.mods.set(mods);
        this.loading.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load mods'));
        this.loading.set(false);
      },
    });
  }

  protected async upload(): Promise<void> {
    const created = await this.uploadDialog.open();
    if (created) this.reload();
  }

  protected async remove(mod: ModDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Remove ${mod.fileName}?`,
      message:
        'It leaves the manifest immediately, and every client deletes its copy on the next launch.',
      confirmLabel: 'Remove',
      destructive: true,
    });
    if (!ok) return;

    this.deleting.update((d) => ({ ...d, [mod.id]: true }));
    this.api.apiModsIdDelete(mod.id).subscribe({
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
