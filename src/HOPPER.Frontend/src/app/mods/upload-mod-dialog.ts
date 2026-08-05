import { ChangeDetectionStrategy, Component, inject, Injectable, signal } from '@angular/core';
import { HttpEventType } from '@angular/common/http';
import { BrnDialogRef } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileArchive, lucideUpload } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import {
  HlmDialogDescription,
  HlmDialogHeader,
  HlmDialogService,
  HlmDialogTitle,
} from '@spartan-ng/helm/dialog';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { ModsService } from '../api/api/mods.service';
import { ModDto } from '../api/model/modDto';
import { formatBytes, messageFrom } from '../shared/utils/format';

@Component({
  selector: 'app-upload-mod-dialog',
  imports: [
    NgIcon,
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmProgressImports,
  ],
  providers: [provideIcons({ lucideFileArchive, lucideUpload })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Upload a mod</h3>
      <p hlmDialogDescription>
        The jar is hashed server-side and stored by content address. Every client picks it up on its
        next launch.
      </p>
    </hlm-dialog-header>

    <!-- The whole zone is the file picker: the hidden input is what actually opens the dialog,
         and the drop handlers cover the drag path. Both land on the same setFile(). -->
    <label
      class="border-input hover:bg-accent/40 flex cursor-pointer flex-col items-center justify-center gap-2 rounded-md border border-dashed p-8 text-center transition-colors"
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
      @if (file(); as f) {
        <span class="font-mono text-sm">{{ f.name }}</span>
        <span class="text-muted-foreground text-xs">{{ sizeLabel() }}</span>
      } @else {
        <span class="text-sm">Drop a .jar here, or click to choose one</span>
        <span class="text-muted-foreground text-xs">Up to 512 MB</span>
      }
      <input type="file" accept=".jar" class="hidden" (change)="onPick($event)" />
    </label>

    @if (uploading()) {
      <div class="flex flex-col gap-1">
        <hlm-progress [value]="progress()">
          <hlm-progress-indicator />
        </hlm-progress>
        <span class="text-muted-foreground text-xs">Uploading… {{ progress() }}%</span>
      </div>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="ghost" type="button" [disabled]="uploading()" (click)="cancel()">
        Cancel
      </button>
      <button hlmBtn type="button" [disabled]="uploading() || !file()" (click)="upload()">
        {{ uploading() ? 'Uploading…' : 'Upload' }}
      </button>
    </div>
  `,
})
export class UploadModDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(ModsService);

  protected readonly file = signal<File | null>(null);
  protected readonly dragging = signal(false);
  protected readonly uploading = signal(false);
  protected readonly progress = signal(0);

  protected sizeLabel(): string {
    const f = this.file();
    return f ? formatBytes(f.size) : '';
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.setFile(input.files?.item(0) ?? null);
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
    this.setFile(event.dataTransfer?.files?.item(0) ?? null);
  }

  private setFile(file: File | null): void {
    if (file && !file.name.toLowerCase().endsWith('.jar')) {
      // The server rejects this too, but catching it here saves uploading tens of megabytes
      // just to be told no.
      this.file.set(null);
      toast.error('Only .jar files can be distributed.');
      return;
    }
    this.file.set(file);
  }

  protected upload(): void {
    const file = this.file();
    if (!file) return;

    this.uploading.set(true);
    this.progress.set(0);

    // 'events' + reportProgress is the only way to get an upload percentage out of the generated
    // client; the body arrives on the final Response event.
    this.api.apiModsPost(file, 'events', true).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          this.progress.set(Math.round((event.loaded / event.total) * 100));
        } else if (event.type === HttpEventType.Response) {
          this.ref.close(event.body as ModDto);
        }
      },
      error: (err) => {
        // A 409 means a mod with that filename already exists; the server's message says so, and
        // it arrives as { error: "..." } rather than as an HTTP error string.
        toast.error(messageFrom(err, 'Failed to upload the mod'));
        this.uploading.set(false);
      },
    });
  }

  protected cancel(): void {
    this.ref.close(null);
  }
}

@Injectable({ providedIn: 'root' })
export class UploadModDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(): Promise<ModDto | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(UploadModDialog, { contentClass: 'sm:max-w-lg' });
      ref.closed$.subscribe((result) => resolve((result as ModDto | null) ?? null));
    });
  }
}
