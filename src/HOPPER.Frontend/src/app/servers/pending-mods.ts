import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import { lucideExternalLink, lucideUpload, lucideX } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ServerImportsService } from '../api/api/serverImports.service';
import { ModDto } from '../api/model/modDto';
import { PendingModDto } from '../api/model/pendingModDto';
import { messageFrom } from '../shared/utils/format';
import {
  pendingLabel,
  pendingProjectUrl,
  pendingReasonDetail,
  pendingReasonLabel,
} from './import-labels';

@Component({
  selector: 'app-pending-mods',
  imports: [NgIcon, HlmBadgeImports, HlmButtonImports],
  providers: [provideIcons({ lucideExternalLink, lucideUpload, lucideX })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="rounded-md border">
      @for (entry of entries(); track entry.id) {
        <li class="flex flex-wrap items-start gap-2 border-b px-3 py-2 last:border-b-0">
          <span class="flex min-w-40 flex-1 flex-col gap-0.5">
            <span class="flex flex-wrap items-center gap-2">
              <span class="truncate text-xs font-medium">{{ name(entry) }}</span>
              <span hlmBadge variant="outline" class="text-xs">{{ reason(entry) }}</span>
            </span>
            <span class="text-muted-foreground text-xs">{{ why(entry) }}</span>
          </span>

          <span class="flex shrink-0 items-center gap-1">
            @if (link(entry); as href) {
              <a
                hlmBtn
                variant="ghost"
                size="sm"
                [href]="href"
                target="_blank"
                rel="noopener noreferrer"
                title="Open the project page"
              >
                <ng-icon name="lucideExternalLink" size="12" />
              </a>
            }

            <!-- The hidden input is the file chooser; the button is what the admin sees. One input
                 per row rather than one shared one, so the file that comes back is unambiguously
                 this entry's - a shared input would need the click and the change event to agree
                 about which row was current. -->
            <input
              #picker
              type="file"
              accept=".jar"
              class="hidden"
              (change)="supply(entry, picker)"
            />
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="busy()[entry.id] === true"
              (click)="picker.click()"
            >
              <ng-icon name="lucideUpload" size="12" />
              {{ busy()[entry.id] === true ? 'Storing…' : 'Supply jar' }}
            </button>
            <button
              hlmBtn
              variant="ghost"
              size="sm"
              type="button"
              title="Drop this entry"
              [disabled]="busy()[entry.id] === true"
              (click)="dismiss(entry)"
            >
              <ng-icon name="lucideX" size="12" />
            </button>
          </span>
        </li>
      }
    </ul>
  `,
})
export class PendingMods {
  private readonly api = inject(ServerImportsService);

  readonly serverId = input.required<string>();
  readonly entries = input.required<ReadonlyArray<PendingModDto>>();

  readonly resolved = output<ModDto>();

  readonly dismissed = output<PendingModDto>();

  protected readonly busy = signal<Record<string, boolean>>({});

  protected name(entry: PendingModDto): string {
    return pendingLabel(entry);
  }

  protected reason(entry: PendingModDto): string {
    return pendingReasonLabel(entry.reason);
  }

  protected why(entry: PendingModDto): string {
    return entry.detail && entry.detail.length > 0
      ? entry.detail
      : pendingReasonDetail(entry.reason);
  }

  protected link(entry: PendingModDto): string | null {
    return pendingProjectUrl(entry);
  }

  protected supply(entry: PendingModDto, picker: HTMLInputElement): void {
    const file = picker.files?.[0] ?? null;

    picker.value = '';
    if (file === null || this.busy()[entry.id] === true) return;

    if (!file.name.toLowerCase().endsWith('.jar')) {
      toast.error('A mod is a .jar.');
      return;
    }

    this.mark(entry.id, true);

    this.api.apiServersIdPendingPendingIdPost(this.serverId(), entry.id, file).subscribe({
      next: (mod: ModDto) => {
        this.mark(entry.id, false);
        toast.success(`${mod.fileName} stored.`);
        this.resolved.emit(mod);
      },
      error: (err) => {
        this.mark(entry.id, false);

        toast.error(messageFrom(err, `Failed to store ${file.name}`));
      },
    });
  }

  protected dismiss(entry: PendingModDto): void {
    if (this.busy()[entry.id] === true) return;

    this.mark(entry.id, true);
    this.api.apiServersIdPendingPendingIdDelete(this.serverId(), entry.id).subscribe({
      next: () => {
        this.mark(entry.id, false);
        this.dismissed.emit(entry);
      },
      error: (err) => {
        this.mark(entry.id, false);
        toast.error(messageFrom(err, 'Failed to drop the entry'));
      },
    });
  }

  private mark(id: string, busy: boolean): void {
    this.busy.update((current) => ({ ...current, [id]: busy }));
  }
}
