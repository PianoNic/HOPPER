import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injectable,
  signal,
} from '@angular/core';
import { HttpEventType } from '@angular/common/http';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideFileArchive, lucideUpload, lucideX } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import {
  HlmDialogDescription,
  HlmDialogHeader,
  HlmDialogService,
  HlmDialogTitle,
} from '@spartan-ng/helm/dialog';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { ServerModsService } from '../api/api/serverMods.service';
import { FailedUploadDto } from '../api/model/failedUploadDto';
import { ModDto } from '../api/model/modDto';
import { ModUploadResultDto } from '../api/model/modUploadResultDto';
import { formatBytes, messageFrom } from '../shared/utils/format';

export type UploadModsDialogContext = { serverId: string };

type UploadState = 'queued' | 'uploading' | 'stored' | 'partial' | 'failed';

interface UploadItem {
  readonly id: number;
  readonly file: File;
  readonly state: UploadState;
  readonly progress: number;
  readonly detail: string;
  readonly errors: ReadonlyArray<string>;
}

const MAX_ROW_ERRORS = 3;

@Component({
  selector: 'app-upload-mods-dialog',
  imports: [
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    ButtonLoading,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmProgressImports,
  ],
  providers: [provideIcons({ lucideCheck, lucideFileArchive, lucideUpload, lucideX })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Upload jars</h3>
      <p hlmDialogDescription>
        Drop as many jars as you like, or a .zip of them and HOPPER will unpack it. Each is hashed
        server-side and stored by content address, so a jar another server already has costs no
        extra disk.
      </p>
    </hlm-dialog-header>
    <label
      class="border-input hover:bg-accent/40 flex cursor-pointer flex-col items-center justify-center gap-2 rounded-md border border-dashed p-6 text-center transition-colors"
      [class.border-primary]="dragging()"
      [class.bg-accent]="dragging()"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      <ng-icon
        [name]="items().length > 0 ? 'lucideFileArchive' : 'lucideUpload'"
        size="24"
        class="text-muted-foreground"
      />
      @if (items().length > 0) {
        <span class="text-sm">{{ picked() }}</span>
        <span class="text-muted-foreground text-xs">Click to add more</span>
      } @else {
        <span class="text-sm">Drop .jar files here, or click to choose them</span>
        <span class="text-muted-foreground text-xs">A .zip of jars works too</span>
      }
      <input type="file" accept=".jar,.zip" multiple class="hidden" (change)="onPick($event)" />
    </label>
    @if (items().length > 0) {
      <ul class="max-h-64 overflow-auto rounded-md border">
        @for (item of items(); track item.id) {
          <li class="flex flex-col gap-1 border-b px-3 py-2 last:border-b-0">
            <div class="flex items-center justify-between gap-2">
              <span class="flex min-w-0 items-center gap-2">
                <ng-icon
                  [name]="item.state === 'stored' ? 'lucideCheck' : 'lucideFileArchive'"
                  size="12"
                  class="text-muted-foreground shrink-0"
                />
                <span class="truncate font-mono text-xs" [title]="item.file.name">
                  {{ item.file.name }}
                </span>
              </span>
              <span class="flex shrink-0 items-center gap-1">
                @if (item.state !== 'queued') {
                  <span hlmBadge [variant]="badgeVariant(item)" class="text-xs">
                    {{ stateLabel(item) }}
                  </span>
                }
                <span class="text-muted-foreground text-xs tabular-nums">{{ sizeOf(item) }}</span>
                @if (item.state === 'queued') {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon"
                    type="button"
                    title="Remove from batch"
                    [disabled]="running()"
                    (click)="drop(item)"
                  >
                    <ng-icon name="lucideX" size="12" />
                  </button>
                }
              </span>
            </div>
            @if (item.state === 'uploading') {
              <div class="bg-muted h-1 w-full overflow-hidden rounded-full">
                <div class="bg-primary h-full transition-all" [style.width.%]="item.progress"></div>
              </div>
            }

            @if (item.detail !== '') {
              <span
                class="text-xs"
                [class.text-muted-foreground]="item.state !== 'failed'"
                [class.text-destructive]="item.state === 'failed'"
                >{{ item.detail }}</span
              >
            }

            @for (error of item.errors; track error) {
              <span class="text-muted-foreground truncate pl-4 font-mono text-xs" [title]="error">
                {{ error }}
              </span>
            }
          </li>
        }
      </ul>
    }

    @if (running()) {
      <div class="flex flex-col gap-1">
        <hlm-progress [value]="overallProgress()">
          <hlm-progress-indicator />
        </hlm-progress>
        <span class="text-muted-foreground text-xs">{{ overallLabel() }}</span>
      </div>
    }

    <div class="flex items-center justify-between gap-2">
      <span class="text-muted-foreground text-xs">{{ outcome() }}</span>
      <span class="flex gap-2">
        <button hlmBtn variant="ghost" type="button" [disabled]="running()" (click)="close()">
          {{ finished() ? 'Close' : 'Cancel' }}
        </button>
        <button
          hlmBtn
          type="button"
          [disabled]="running() || queuedCount() === 0"
          [loading]="running()"
          (click)="start()"
        >
          {{ label() }}
        </button>
      </span>
    </div>
  `,
})
export class UploadModsDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(ServerModsService);
  private readonly ctx = injectBrnDialogContext<UploadModsDialogContext>();

  protected readonly items = signal<ReadonlyArray<UploadItem>>([]);
  protected readonly dragging = signal(false);
  protected readonly running = signal(false);

  private readonly uploaded = signal<ReadonlyArray<ModDto>>([]);
  private readonly failed = signal<ReadonlyArray<FailedUploadDto>>([]);

  private nextId = 0;

  protected readonly queuedCount = computed(
    () => this.items().filter((i) => i.state === 'queued').length,
  );

  protected readonly finished = computed(
    () => !this.running() && this.items().some((i) => i.state !== 'queued'),
  );

  protected readonly picked = computed(() => {
    const list = this.items();
    const bytes = list.reduce((sum, i) => sum + i.file.size, 0);
    return `${list.length} file${list.length === 1 ? '' : 's'} · ${formatBytes(bytes)}`;
  });

  protected readonly label = computed(() => {
    const queued = this.queuedCount();
    if (this.running()) return 'Uploading';
    if (queued === 0) return 'Upload';
    return queued === 1 ? 'Upload' : `Upload ${queued} files`;
  });

  protected readonly overallProgress = computed(() => {
    const list = this.items();
    const total = list.reduce((sum, i) => sum + i.file.size, 0);
    if (total === 0) return 0;

    const sent = list.reduce((sum, i) => {
      if (i.state === 'queued') return sum;
      if (i.state === 'uploading') return sum + (i.file.size * i.progress) / 100;
      return sum + i.file.size;
    }, 0);

    return Math.round((sent / total) * 100);
  });

  protected readonly overallLabel = computed(() => {
    const list = this.items();
    const done = list.filter((i) => i.state !== 'queued' && i.state !== 'uploading').length;
    return `Uploading ${done + 1} of ${list.length}`;
  });

  protected readonly outcome = computed(() => {
    if (this.running()) return '';
    const stored = this.uploaded().length;
    const rejected = this.failed().length;
    if (stored === 0 && rejected === 0) return '';
    return `${stored} stored · ${rejected} rejected`;
  });

  protected sizeOf(item: UploadItem): string {
    return formatBytes(item.file.size);
  }

  protected stateLabel(item: UploadItem): string {
    switch (item.state) {
      case 'uploading':
        return `${item.progress}%`;
      case 'stored':
        return 'stored';
      case 'partial':
        return 'partial';
      default:
        return 'rejected';
    }
  }

  protected badgeVariant(item: UploadItem): 'default' | 'secondary' | 'destructive' | 'outline' {
    switch (item.state) {
      case 'uploading':
        return 'outline';
      case 'stored':
        return 'default';
      case 'partial':
        return 'secondary';
      default:
        return 'destructive';
    }
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.addFiles(input.files);

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
    this.addFiles(event.dataTransfer?.files ?? null);
  }

  protected drop(item: UploadItem): void {
    this.items.update((list) => list.filter((i) => i.id !== item.id));
  }

  private addFiles(list: FileList | null): void {
    if (!list) return;

    const accepted: UploadItem[] = [];
    const rejected: string[] = [];
    for (const file of Array.from(list)) {
      const name = file.name.toLowerCase();
      if (name.endsWith('.jar') || name.endsWith('.zip')) {
        accepted.push({
          id: this.nextId++,
          file,
          state: 'queued',
          progress: 0,
          detail: '',
          errors: [],
        });
      } else {
        rejected.push(file.name);
      }
    }

    if (rejected.length > 0) {
      toast.error('Only .jar files and .zip archives of jars can be uploaded.');
    }

    if (accepted.length > 0) this.items.update((current) => [...current, ...accepted]);
  }

  protected start(): void {
    if (this.running() || this.queuedCount() === 0) return;
    this.running.set(true);
    this.next();
  }

  private next(): void {
    const item = this.items().find((i) => i.state === 'queued');
    if (!item) {
      this.running.set(false);
      this.summarise();
      return;
    }

    this.patch(item.id, { state: 'uploading', progress: 0, detail: '', errors: [] });

    this.api.apiServersIdModsPost(this.ctx.serverId, [item.file], 'events', true).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          this.patch(item.id, { progress: Math.round((event.loaded / event.total) * 100) });
        } else if (event.type === HttpEventType.Response) {
          this.settle(item, event.body as ModUploadResultDto);
          this.next();
        }
      },
      error: (err) => {
        const message = messageFrom(err, 'Upload failed');
        this.patch(item.id, { state: 'failed', progress: 0, detail: message });
        this.failed.update((list) => [...list, { fileName: item.file.name, error: message }]);
        toast.error(`${item.file.name}: ${message}`);

        this.next();
      },
    });
  }

  private settle(item: UploadItem, result: ModUploadResultDto): void {
    const stored = result.uploaded;
    const rejected = result.failed;

    this.uploaded.update((list) => [...list, ...stored]);
    this.failed.update((list) => [...list, ...rejected]);

    const errors = rejected.slice(0, MAX_ROW_ERRORS).map((f) => `${f.fileName}: ${f.error}`);
    const more = rejected.length - errors.length;
    if (more > 0) errors.push(`…and ${more} more`);

    if (stored.length === 0) {
      const detail = rejected.length === 1 ? rejected[0].error : 'Nothing in this file was stored.';
      this.patch(item.id, { state: 'failed', progress: 100, detail, errors });
      toast.error(`${item.file.name}: ${detail}`);
      return;
    }

    const jars = `${stored.length} jar${stored.length === 1 ? '' : 's'} stored`;

    if (rejected.length > 0) {
      const detail = `${jars}, ${rejected.length} rejected`;
      this.patch(item.id, { state: 'partial', progress: 100, detail, errors });
      toast.error(`${item.file.name}: ${rejected.length} rejected`);
      return;
    }

    this.patch(item.id, { state: 'stored', progress: 100, detail: jars, errors: [] });
  }

  private summarise(): void {
    const stored = this.uploaded().length;
    if (stored > 0) toast.success(`${stored} jar${stored === 1 ? '' : 's'} uploaded.`);
  }

  private patch(id: number, changes: Partial<UploadItem>): void {
    this.items.update((list) => list.map((i) => (i.id === id ? { ...i, ...changes } : i)));
  }

  protected close(): void {
    if (this.uploaded().length === 0 && this.failed().length === 0) {
      this.ref.close(null);
      return;
    }

    this.ref.close({ uploaded: [...this.uploaded()], failed: [...this.failed()] });
  }
}

@Injectable({ providedIn: 'root' })
export class UploadModsDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(context: UploadModsDialogContext): Promise<ModUploadResultDto | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(UploadModsDialog, { context, contentClass: 'sm:max-w-lg' });
      ref.closed$.subscribe((result) => resolve((result as ModUploadResultDto | null) ?? null));
    });
  }
}
