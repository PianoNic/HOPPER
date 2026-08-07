import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injectable,
  signal,
} from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideExternalLink, lucidePackage } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import {
  HlmDialogDescription,
  HlmDialogHeader,
  HlmDialogService,
  HlmDialogTitle,
} from '@spartan-ng/helm/dialog';
import { ModrinthService } from '../api/api/modrinth.service';
import { ModrinthVersionDto } from '../api/model/modrinthVersionDto';
import { formatBytes, messageFrom, toNumber } from '../shared/utils/format';
import { modrinthProjectUrl, versionTypeLabel } from './mod-labels';

export type ModrinthVersionDialogContext = {
  serverId: string;
  projectId: string;
  projectTitle: string;
  slug: string | null;
  loader: string;
  gameVersion: string;
};

export type ModrinthVersionPick = { projectId: string; versionId: string; title: string };

@Component({
  selector: 'app-modrinth-version-dialog',
  imports: [
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmSpinnerImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
  ],
  providers: [provideIcons({ lucideCheck, lucideExternalLink, lucidePackage })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ ctx.projectTitle }}</h3>
      <p hlmDialogDescription>
        Versions built for {{ ctx.loader }} {{ ctx.gameVersion }}, newest first. Picking one shows
        what it would pull in before anything is downloaded.
      </p>
    </hlm-dialog-header>

    <div class="max-h-96 min-h-0 flex-1 overflow-auto">
      @if (loading()) {
        <p class="text-muted-foreground flex items-center justify-center gap-2 p-6 text-sm">
          <hlm-spinner aria-label="Loading versions" />
          Loading versions
        </p>
      } @else if (versions().length === 0) {
        <div
          class="text-muted-foreground flex flex-col items-center justify-center gap-2 p-8 text-center text-sm"
        >
          <ng-icon name="lucidePackage" size="24" class="opacity-60" />
          <p>No version of this mod is built for {{ ctx.loader }} {{ ctx.gameVersion }}.</p>
          <p class="max-w-sm text-xs">
            The author may not have shipped one yet. Check the project page for which loaders and
            Minecraft versions it does support.
          </p>
        </div>
      } @else {
        <ul class="flex flex-col gap-1">
          @for (v of versions(); track v.id) {
            <li>
              <button
                type="button"
                class="hover:bg-accent flex w-full items-start gap-3 rounded-md border p-2 text-left disabled:cursor-not-allowed disabled:opacity-60"
                [disabled]="!installable(v)"
                (click)="pick(v)"
              >
                <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                  <div class="flex items-center gap-2">
                    <span class="truncate text-sm font-medium">{{ label(v) }}</span>
                    <span hlmBadge [variant]="channelVariant(v)" class="text-xs">
                      {{ channel(v) }}
                    </span>
                    @if (v.installed) {
                      <span hlmBadge variant="secondary" class="text-xs">
                        <ng-icon name="lucideCheck" size="12" />
                        on this server
                      </span>
                    }
                  </div>
                  <span class="text-muted-foreground truncate font-mono text-xs">
                    {{ v.fileName ?? 'no installable file' }}
                  </span>
                </div>
                <div class="text-muted-foreground flex shrink-0 flex-col items-end gap-0.5 text-xs">
                  <span class="font-mono">{{ size(v) }}</span>
                  <span>{{ published(v) }}</span>
                </div>
              </button>
            </li>
          }
        </ul>
      }
    </div>

    <div class="flex items-center justify-between gap-2">
      @if (projectUrl(); as url) {
        <a
          hlmBtn
          variant="ghost"
          size="sm"
          [href]="url"
          target="_blank"
          rel="noopener noreferrer"
        >
          <ng-icon name="lucideExternalLink" size="14" />
          Open on Modrinth
        </a>
      } @else {
        <span></span>
      }
      <button hlmBtn variant="ghost" type="button" (click)="cancel()">Cancel</button>
    </div>
  `,
})
export class ModrinthVersionDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(ModrinthService);
  protected readonly ctx = injectBrnDialogContext<ModrinthVersionDialogContext>();

  protected readonly versions = signal<ReadonlyArray<ModrinthVersionDto>>([]);
  protected readonly loading = signal(true);

  protected readonly projectUrl = computed(() => modrinthProjectUrl(this.ctx.slug));

  constructor() {
    this.api
      .apiModrinthProjectsIdOrSlugVersionsGet(
        this.ctx.projectId,
        this.ctx.loader,
        this.ctx.gameVersion,
        this.ctx.serverId,
      )
      .subscribe({
        next: (list) => {
          this.versions.set(list);
          this.loading.set(false);
        },
        error: (err) => {
          toast.error(messageFrom(err, 'Failed to load the versions of this mod'));
          this.loading.set(false);
        },
      });
  }

  protected installable(version: ModrinthVersionDto): boolean {
    return (version.fileName ?? '') !== '';
  }

  protected label(version: ModrinthVersionDto): string {
    return version.versionNumber ?? version.name ?? version.id;
  }

  protected channel(version: ModrinthVersionDto): string {
    return versionTypeLabel(version.versionType);
  }

  protected channelVariant(version: ModrinthVersionDto): 'secondary' | 'outline' {
    return (version.versionType ?? '').toLowerCase() === 'release' ? 'secondary' : 'outline';
  }

  protected size(version: ModrinthVersionDto): string {
    return formatBytes(toNumber(version.fileSize));
  }

  protected published(version: ModrinthVersionDto): string {
    if (!version.datePublished) return '';
    return version.datePublished.slice(0, 10);
  }

  protected pick(version: ModrinthVersionDto): void {
    if (!this.installable(version)) return;
    this.ref.close({
      projectId: version.projectId,
      versionId: version.id,
      title: this.ctx.projectTitle,
    });
  }

  protected cancel(): void {
    this.ref.close(null);
  }
}

@Injectable({ providedIn: 'root' })
export class ModrinthVersionDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(context: ModrinthVersionDialogContext): Promise<ModrinthVersionPick | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(ModrinthVersionDialog, { context, contentClass: 'sm:max-w-lg' });
      ref.closed$.subscribe((result) => resolve((result as ModrinthVersionPick | null) ?? null));
    });
  }
}
