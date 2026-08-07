import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  Injectable,
  signal,
} from '@angular/core';
import { HttpClient, HttpEventType } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval, startWith, Subscription, switchMap } from 'rxjs';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideClipboardList,
  lucideFileArchive,
  lucideLink,
  lucideUpload,
} from '@ng-icons/lucide';

import { simpleCurseforge, simpleModrinth } from '@ng-icons/simple-icons';
import { hopperPrism } from '../shared/icons/prism-icon';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import {
  HlmDialogDescription,
  HlmDialogHeader,
  HlmDialogService,
  HlmDialogTitle,
} from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { ServerImportsService } from '../api/api/serverImports.service';
import { ModImportDto } from '../api/model/modImportDto';
import { PendingModDto } from '../api/model/pendingModDto';
import { BASE_PATH } from '../api/variables';
import { formatBytes, messageFrom, toNumber } from '../shared/utils/format';
import {
  IMPORT_STATUS,
  importStatusLabel,
  isImportPending,
  packFormatLabel,
} from './import-labels';
import { PendingMods } from './pending-mods';

export type ImportPackDialogContext = { serverId: string };

export type ImportPackResult = { import: ModImportDto; openPending: boolean };

type PackSource = 'modrinth' | 'curseforge' | 'prism';

const POLL_MS = 2000;

@Component({
  selector: 'app-import-pack-dialog',
  imports: [
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    ButtonLoading,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
    HlmProgressImports,
    PendingMods,
  ],
  providers: [
    provideIcons({
      simpleCurseforge,
      simpleModrinth,
      hopperPrism,
      lucideClipboardList,
      lucideFileArchive,
      lucideLink,
      lucideUpload,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Import a modpack</h3>
      <p hlmDialogDescription>
        Upload a pack or paste a link to one. HOPPER reads its manifest, fetches what it can and
        stores the jars on this server. Whatever it cannot fetch is listed at the end for you to
        collect by hand.
      </p>
    </hlm-dialog-header>

    @if (job() === null) {
      <div class="flex flex-col gap-3">
        <div class="flex flex-col gap-1.5">
          <span hlmLabel>Pack type</span>
          <div class="flex gap-2">
            <button
              hlmBtn
              type="button"
              size="sm"
              [variant]="source() === 'modrinth' ? 'default' : 'outline'"
              (click)="choose('modrinth')"
            >
              <ng-icon name="simpleModrinth" size="14" />
              Modrinth
            </button>
            <button
              hlmBtn
              type="button"
              size="sm"
              [variant]="source() === 'curseforge' ? 'default' : 'outline'"
              (click)="choose('curseforge')"
            >
              <ng-icon name="simpleCurseforge" size="14" />
              CurseForge
            </button>
            <button
              hlmBtn
              type="button"
              size="sm"
              [variant]="source() === 'prism' ? 'default' : 'outline'"
              (click)="choose('prism')"
            >
              <ng-icon name="hopperPrism" size="14" />
              Prism
            </button>
          </div>
          <p class="text-muted-foreground text-xs">{{ sourceHint() }}</p>
        </div>

        <!-- File and URL are one choice, not two fields: filling either clears the other, so the
             request that goes out is never ambiguous about which one the admin meant. -->
        <label
          class="border-input hover:bg-accent/40 flex cursor-pointer flex-col items-center justify-center gap-2 rounded-md border border-dashed p-6 text-center transition-colors"
          [class.border-primary]="dragging()"
          [class.bg-accent]="dragging()"
          (dragover)="onDragOver($event)"
          (dragleave)="onDragLeave($event)"
          (drop)="onDrop($event)"
        >
          <ng-icon
            [name]="file() ? 'lucideFileArchive' : 'lucideUpload'"
            size="24"
            class="text-muted-foreground"
          />
          @if (file(); as picked) {
            <span class="truncate font-mono text-sm">{{ picked.name }}</span>
            <span class="text-muted-foreground text-xs">{{ pickedSize() }} · click to replace</span>
          } @else {
            <span class="text-sm">Drop the pack here, or click to choose it</span>
            <span class="text-muted-foreground text-xs">{{ acceptHint() }}</span>
          }
          <input type="file" [accept]="accept()" class="hidden" (change)="onPick($event)" />
        </label>

        <div class="text-muted-foreground flex items-center gap-2 text-xs">
          <span class="bg-border h-px flex-1"></span>
          or
          <span class="bg-border h-px flex-1"></span>
        </div>

        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="pack-url">Link to the pack</label>
          <div class="relative">
            <ng-icon
              name="lucideLink"
              size="14"
              class="text-muted-foreground absolute left-2 top-1/2 -translate-y-1/2"
            />
            <input
              hlmInput
              id="pack-url"
              class="w-full pl-7 font-mono text-xs"
              [placeholder]="urlPlaceholder()"
              [value]="url()"
              [disabled]="submitting()"
              (input)="onUrl($event)"
            />
          </div>
        </div>
      </div>
    }

    @if (submitting()) {
      <div class="flex flex-col gap-1">
        <hlm-progress [value]="uploadProgress()">
          <hlm-progress-indicator />
        </hlm-progress>
        <span class="text-muted-foreground text-xs"
          >Uploading the pack… {{ uploadProgress() }}%</span
        >
      </div>
    }

    @if (job(); as row) {
      <div class="flex flex-col gap-3 rounded-md border p-3">
        <div class="flex items-center justify-between gap-2">
          <span class="flex min-w-0 flex-col">
            <span class="truncate text-sm font-medium" [title]="row.sourceName">
              {{ row.sourceName }}
            </span>
            <span class="text-muted-foreground text-xs">{{ format() }}</span>
          </span>
          <span hlmBadge [variant]="statusVariant()" class="text-xs">{{ status() }}</span>
        </div>

        <!-- No progress bar while it runs: a pack has no total until the manifest is parsed, and an
             indeterminate hlm-progress renders as a full bar here, which reads as finished. The
             counters below move on every file, which is the honest signal of life. -->
        <dl class="grid grid-cols-4 gap-2 text-center">
          @for (counter of counters(); track counter.label) {
            <div class="rounded-md border p-2" [title]="counter.hint">
              <dt class="text-muted-foreground text-xs">{{ counter.label }}</dt>
              <dd class="text-lg font-semibold tabular-nums">{{ counter.value }}</dd>
            </div>
          }
        </dl>

        @if (row.error) {
          <p class="text-xs" [class]="noteClass()">{{ row.error }}</p>
        }

        @if (watching()) {
          <p class="text-muted-foreground text-xs">
            The import runs on the server. Closing this dialog does not stop it - the counters keep
            moving and the mods appear on the list as they land.
          </p>
        }
      </div>
    }

    @if (pending().length > 0) {
      <div class="flex flex-col gap-2">
        <h4 class="text-sm font-medium">
          Fetch by hand
          <span class="text-muted-foreground font-normal">· {{ pendingSummary() }}</span>
        </h4>
        <p class="text-muted-foreground text-xs">
          Open each project page, download the file, then hand it straight back with
          <strong>Supply jar</strong> - that is what clears the row, checks it against the hash the
          pack declared and stores it under the name the pack expects. Anything left here stays on
          this server: closing the dialog loses nothing, the list is on the
          <strong>Fetch by hand</strong> page.
        </p>

        <div class="max-h-56 overflow-auto">
          <app-pending-mods
            [serverId]="ctx.serverId"
            [entries]="pending()"
            (resolved)="settled()"
            (dismissed)="drop($event)"
          />
        </div>
      </div>
    }

    <div class="flex justify-end gap-2">
      <!-- Hidden once the import is done and left nothing to do: a "Close" next to a "Done" that
           both close the same dialog is two buttons for one act. -->
      @if (job() === null || watching() || pending().length > 0) {
        <button
          hlmBtn
          variant="ghost"
          type="button"
          [disabled]="submitting()"
          (click)="close(false)"
        >
          {{ job() === null ? 'Cancel' : 'Close' }}
        </button>
      }
      @if (job() === null) {
        <button hlmBtn type="button" [disabled]="!canSubmit()" (click)="submit()" [loading]="submitting()">
          {{ submitting() ? 'Uploading' : 'Import' }}
        </button>
      } @else if (pending().length > 0) {
        <button hlmBtn type="button" (click)="close(true)">
          <ng-icon name="lucideClipboardList" size="14" />
          Finish this later
        </button>
      } @else if (!watching()) {
        <button hlmBtn type="button" (click)="close(false)">Done</button>
      }
    </div>
  `,
})
export class ImportPackDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(ServerImportsService);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly ctx = injectBrnDialogContext<ImportPackDialogContext>();

  private readonly basePath = inject(BASE_PATH, { optional: true }) ?? '';

  protected readonly source = signal<PackSource>('modrinth');
  protected readonly file = signal<File | null>(null);
  protected readonly url = signal('');
  protected readonly dragging = signal(false);
  protected readonly submitting = signal(false);
  protected readonly uploadProgress = signal(0);

  protected readonly job = signal<ModImportDto | null>(null);
  protected readonly pending = signal<ReadonlyArray<PendingModDto>>([]);

  private poll: Subscription | null = null;

  protected readonly canSubmit = computed(
    () => !this.submitting() && (this.file() !== null || this.url().trim() !== ''),
  );

  protected readonly watching = computed(() => {
    const row = this.job();
    return row !== null && isImportPending(row.status);
  });

  protected readonly status = computed(() => {
    const row = this.job();
    return row === null ? '' : importStatusLabel(row.status);
  });

  protected readonly statusVariant = computed<'default' | 'secondary' | 'destructive' | 'outline'>(
    () => {
      const row = this.job();
      if (row === null) return 'outline';
      if (row.status === IMPORT_STATUS.failed) return 'destructive';
      if (row.status === IMPORT_STATUS.completed) return 'default';
      return 'outline';
    },
  );

  protected readonly noteClass = computed(() =>
    this.job()?.status === IMPORT_STATUS.completed ? 'text-muted-foreground' : 'text-destructive',
  );

  protected readonly format = computed(() => {
    const row = this.job();
    return row === null ? '' : packFormatLabel(row.format);
  });

  protected readonly counters = computed(() => {
    const row = this.job();
    if (row === null) return [];
    return [
      {
        label: 'Stored',
        value: toNumber(row.importedCount),
        hint: 'Jars stored on this server by this import.',
      },
      {
        label: 'Skipped',
        value: toNumber(row.skippedCount),
        hint: 'Files the pack listed that were not stored: non-mod files, mods the pack marks as client-unsupported, and names already on this server.',
      },
      {
        label: 'Pending',
        value: toNumber(row.pendingCount),
        hint: 'Mods HOPPER could not fetch. Supply the jar by hand to finish them.',
      },
      {
        label: 'Failed',
        value: toNumber(row.failedCount),
        hint: 'Files that could not be read or stored. The reason is listed below.',
      },
    ];
  });

  protected readonly pendingSummary = computed(() => {
    const open = this.pending().length;
    return `${open} still to collect`;
  });

  protected readonly accept = computed(() =>
    this.source() === 'modrinth' ? '.mrpack,.zip' : '.zip',
  );

  protected readonly acceptHint = computed(() => {
    switch (this.source()) {
      case 'modrinth':
        return 'A .mrpack, or a zip of an exported instance';
      case 'prism':
        return 'The instance zip exported from Prism or MultiMC';
      default:
        return 'The pack zip with manifest.json in it';
    }
  });

  protected readonly urlPlaceholder = computed(() => {
    switch (this.source()) {
      case 'modrinth':
        return 'https://cdn.modrinth.com/data/…/versions/…/pack.mrpack';
      case 'prism':
        return 'https://example.com/MyInstance.zip';
      default:
        return 'https://example.com/AllTheMods-9-1.1.1.zip';
    }
  });

  protected readonly sourceHint = computed(() => {
    switch (this.source()) {
      case 'modrinth':
        return 'Everything a .mrpack lists is downloaded and hash-checked automatically, including the loose jars in its overrides folder.';
      case 'prism':
        return 'A Prism or MultiMC instance export wraps one of the other two formats. HOPPER strips the instance wrapper and imports whatever is inside, so it behaves like that format.';
      default:
        return 'A CurseForge pack names its mods by project and file id only. Without a CurseForge API key configured, HOPPER takes the jars out of overrides/ and lists every other entry for you to fetch by hand.';
    }
  });

  protected readonly pickedSize = computed(() => {
    const picked = this.file();
    return picked === null ? '' : formatBytes(picked.size);
  });

  protected settled(): void {
    const row = this.job();
    if (row !== null) this.loadPending(row);
  }

  protected drop(entry: PendingModDto): void {
    this.pending.update((list) => list.filter((p) => p.id !== entry.id));
  }

  protected choose(source: PackSource): void {
    this.source.set(source);
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.take(input.files?.[0] ?? null);
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.take(event.dataTransfer?.files?.[0] ?? null);
  }

  protected onUrl(event: Event): void {
    this.url.set((event.target as HTMLInputElement).value);

    if (this.url().trim() !== '') this.file.set(null);
  }

  private take(picked: File | null): void {
    if (picked === null) return;

    const name = picked.name.toLowerCase();
    if (!name.endsWith('.mrpack') && !name.endsWith('.zip')) {
      toast.error('A pack is a .mrpack or a .zip.');
      return;
    }

    this.file.set(picked);
    this.url.set('');
  }

  protected submit(): void {
    if (!this.canSubmit()) return;

    const picked = this.file();
    if (picked !== null) {
      this.submitFile(picked);
      return;
    }

    const url = this.url().trim();
    if (url !== '') this.submitUrl(url);
  }

  private submitFile(picked: File): void {
    this.submitting.set(true);
    this.uploadProgress.set(0);

    this.api.apiServersIdImportsPost(this.ctx.serverId, picked, 'events', true).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          this.uploadProgress.set(Math.round((event.loaded / event.total) * 100));
        } else if (event.type === HttpEventType.Response) {
          this.submitting.set(false);
          this.accepted(event.body as ModImportDto);
        }
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to start the import'));
        this.submitting.set(false);
      },
    });
  }

  private submitUrl(url: string): void {
    this.submitting.set(true);

    this.http
      .post<ModImportDto>(`${this.basePath}/api/servers/${this.ctx.serverId}/imports`, { url })
      .subscribe({
        next: (row) => {
          this.submitting.set(false);
          this.accepted(row);
        },
        error: (err) => {
          toast.error(messageFrom(err, 'Failed to start the import'));
          this.submitting.set(false);
        },
      });
  }

  private accepted(row: ModImportDto): void {
    this.job.set(row);
    if (!isImportPending(row.status)) {
      this.loadPending(row);
      return;
    }
    this.watch(row.id);
  }

  private watch(importId: string): void {
    this.poll?.unsubscribe();
    this.poll = interval(POLL_MS)
      .pipe(
        startWith(0),
        switchMap(() => this.api.apiServersIdImportsImportIdGet(this.ctx.serverId, importId)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (row) => {
          this.job.set(row);
          if (isImportPending(row.status)) return;

          this.poll?.unsubscribe();
          this.poll = null;
          this.loadPending(row);
        },
        error: (err) => {
          toast.error(messageFrom(err, 'Lost track of the import'));
          this.poll?.unsubscribe();
          this.poll = null;
        },
      });
  }

  private loadPending(row: ModImportDto): void {
    if (row.status === IMPORT_STATUS.failed) {
      toast.error(row.error ?? 'The import failed.');
      return;
    }

    this.api.apiServersIdPendingGet(this.ctx.serverId).subscribe({
      next: (entries) => this.pending.set(entries.filter((p) => p.importId === row.id)),
      error: (err) => toast.error(messageFrom(err, 'Failed to load the pending list')),
    });
  }

  protected close(openPending: boolean): void {
    this.poll?.unsubscribe();
    this.poll = null;

    const row = this.job();
    this.ref.close(row === null ? null : { import: row, openPending });
  }
}

@Injectable({ providedIn: 'root' })
export class ImportPackDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(context: ImportPackDialogContext): Promise<ImportPackResult | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(ImportPackDialog, { context, contentClass: 'sm:max-w-2xl' });
      ref.closed$.subscribe((result) => resolve((result as ImportPackResult | null) ?? null));
    });
  }
}
