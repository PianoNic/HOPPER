import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injectable,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideDownload, lucideLink, lucidePackage, lucideTriangleAlert } from '@ng-icons/lucide';
import { simpleCurseforge, simpleModrinth } from '@ng-icons/simple-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import {
  HlmDialogDescription,
  HlmDialogHeader,
  HlmDialogService,
  HlmDialogTitle,
} from '@spartan-ng/helm/dialog';
import { hopperPrism } from '../shared/icons/prism-icon';
import { ServerExportService } from '../api/api/serverExport.service';
import { ModDto } from '../api/model/modDto';
import { ServerDto } from '../api/model/serverDto';
import { formatBytes } from '../shared/utils/format';
import {
  downloadBlob,
  fileNameFromDisposition,
  messageFromBlobError,
} from '../shared/utils/download';
import { PACK_FORMAT } from './import-labels';
import { MOD_LOADER, modLoaderLabel } from './mod-labels';
import { PackSplit, packSplit } from './pack-split';

export type ExportPackDialogContext = {
  server: ServerDto;
  mods: ReadonlyArray<ModDto>;
};

export type ExportPackResult = { format: number; fileName: string; bytes: number };

type ExportOption = {
  format: number;
  label: string;
  icon: string;

  hint: string;
  split: PackSplit;
};

@Component({
  selector: 'app-export-pack-dialog',
  imports: [NgIcon, HlmButtonImports,
    ButtonLoading, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription],
  providers: [
    provideIcons({
      simpleModrinth,
      simpleCurseforge,
      hopperPrism,
      lucideDownload,
      lucideLink,
      lucidePackage,
      lucideTriangleAlert,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Export {{ ctx.server.name }} as a pack</h3>
      <p hlmDialogDescription>
        A portable copy of this server's mod list that a launcher can import. It carries no HOPPER
        URL and no token, so it works for someone who has never heard of this deployment.
      </p>
    </hlm-dialog-header>

    @if (!platformReady()) {
      <!-- A state, not an error. All three formats name an exact Minecraft version and loader in
           their manifest, and there is nothing honest to guess from a mod list. -->
      <div
        class="text-muted-foreground flex flex-col items-center justify-center gap-2 p-6 text-center text-sm"
      >
        <ng-icon name="lucideTriangleAlert" size="24" class="opacity-60" />
        <p>This server has no Minecraft version or loader set.</p>
        <p class="max-w-sm text-xs">
          Every pack format names both in its manifest, and a launcher refuses one that does not.
          Set them on the Servers page and export from here afterwards.
        </p>
      </div>
    } @else {
      <div class="flex flex-col gap-3">
        <p class="text-muted-foreground text-xs">
          Exporting for
          <strong class="text-foreground">Minecraft {{ ctx.server.minecraftVersion }}</strong>
          on
          <strong class="text-foreground">{{ loaderLabel() }} {{ ctx.server.loaderVersion }}</strong
          >. A mod HOPPER knows the origin of becomes a manifest line the launcher fetches itself
          and costs nothing to download; every other jar is copied into the archive. That is what
          decides whether this file is kilobytes or hundreds of megabytes.
        </p>

        <ul class="flex flex-col gap-2">
          @for (option of options(); track option.format) {
            <li>
              <button
                type="button"
                class="hover:bg-accent/40 flex w-full items-start gap-3 rounded-md border p-3 text-left"
                [class.border-primary]="format() === option.format"
                [class.bg-accent]="format() === option.format"
                (click)="choose(option)"
              >
                <ng-icon [name]="option.icon" size="18" class="mt-0.5 shrink-0" />
                <span class="flex min-w-0 flex-1 flex-col gap-1">
                  <span class="text-sm font-medium">{{ option.label }}</span>
                  <span class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                    @if (option.split.manifestEntries > 0) {
                      <span class="inline-flex items-center gap-1">
                        <ng-icon name="lucideLink" size="12" />
                        {{ option.split.manifestEntries }} as manifest entries
                      </span>
                    }
                    <span class="inline-flex items-center gap-1">
                      <ng-icon name="lucidePackage" size="12" />
                      {{ bundledLabel(option) }}
                    </span>
                    <span class="text-muted-foreground font-mono">{{ sizeLabel(option) }}</span>
                  </span>
                  <span class="text-muted-foreground text-xs">{{ option.hint }}</span>
                </span>
              </button>
            </li>
          }
        </ul>

        <!-- Said once, plainly. Jars are already compressed archives, so deflate takes single-digit
             percent off and the quoted number is the one to plan around rather than a floor. -->
        <p class="text-muted-foreground text-xs">
          Sizes are the jars that go inside, measured before the archive compresses them, so expect
          a file about this big. Anything HOPPER cannot put in - a stored jar that has gone missing
          - is reported when the download finishes.
        </p>
      </div>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="ghost" type="button" [disabled]="busy()" (click)="cancel()">
        Cancel
      </button>
      @if (platformReady()) {
        <button hlmBtn type="button" [disabled]="busy()" (click)="download()" [loading]="busy()">
          <ng-icon name="lucideDownload" size="14" />
          {{ busy() ? 'Building' : downloadLabel() }}
        </button>
      } @else {
        <button hlmBtn type="button" (click)="goToServers()">Go to Servers</button>
      }
    </div>
  `,
})
export class ExportPackDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(ServerExportService);
  private readonly router = inject(Router);
  protected readonly ctx = injectBrnDialogContext<ExportPackDialogContext>();

  protected readonly format = signal<number>(PACK_FORMAT.modrinth);
  protected readonly busy = signal(false);

  protected readonly platformReady = computed(
    () =>
      (this.ctx.server.minecraftVersion ?? '') !== '' &&
      this.ctx.server.loader !== MOD_LOADER.unknown &&
      (this.ctx.server.loaderVersion ?? '') !== '',
  );

  protected readonly loaderLabel = computed(() => modLoaderLabel(this.ctx.server.loader));

  protected readonly options = computed<ReadonlyArray<ExportOption>>(() => {
    const mods = this.ctx.mods;
    return [
      {
        format: PACK_FORMAT.modrinth,
        label: 'Modrinth pack (.mrpack)',
        icon: 'simpleModrinth',
        hint: 'Mods added from Modrinth are listed with their CDN link and hashes. Everything else rides along in overrides/mods/.',
        split: packSplit(mods, PACK_FORMAT.modrinth),
      },
      {
        format: PACK_FORMAT.curseForge,
        label: 'CurseForge pack (.zip)',
        icon: 'simpleCurseforge',

        hint: 'A CurseForge manifest entry is a CurseForge project and file id, which a Modrinth or hand-uploaded jar does not have, so those ship inside overrides/mods/.',
        split: packSplit(mods, PACK_FORMAT.curseForge),
      },
      {
        format: PACK_FORMAT.prismInstance,
        label: 'Prism instance (.zip)',
        icon: 'hopperPrism',
        hint: 'A ready-to-import instance directory. It has no manifest to reference anything from, so every jar is a real file in minecraft/mods/.',
        split: packSplit(mods, PACK_FORMAT.prismInstance),
      },
    ];
  });

  protected readonly selected = computed(
    () => this.options().find((o) => o.format === this.format()) ?? this.options()[0],
  );

  protected readonly downloadLabel = computed(() => `Download ${this.sizeLabel(this.selected())}`);

  protected bundledLabel(option: ExportOption): string {
    const count = option.split.bundledFiles;
    if (count === 0) return 'nothing bundled';
    return `${count} bundled as ${count === 1 ? 'a file' : 'files'}`;
  }

  protected sizeLabel(option: ExportOption): string {
    if (option.split.bundledFiles === 0) return 'a few KB';
    return `about ${formatBytes(option.split.bundledBytes)}`;
  }

  protected choose(option: ExportOption): void {
    if (this.busy()) return;
    this.format.set(option.format);
  }

  protected download(): void {
    if (this.busy()) return;
    const option = this.selected();
    this.busy.set(true);

    this.api.apiServersIdExportGet(this.ctx.server.id, option.format, 'response').subscribe({
      next: async (response) => {
        const blob = response.body as unknown as Blob | null;
        if (!blob) {
          this.busy.set(false);
          toast.error('The export came back empty');
          return;
        }

        const fileName = fileNameFromDisposition(
          response.headers.get('Content-Disposition'),
          `${this.ctx.server.slug}-export.${this.extension(option.format)}`,
        );
        downloadBlob(blob, fileName);

        const warnings = response.headers.get('X-Hopper-Export-Warnings');
        if (warnings) toast.warning(warnings);

        this.busy.set(false);
        this.ref.close({ format: option.format, fileName, bytes: blob.size });
      },
      error: async (err: unknown) => {
        this.busy.set(false);

        toast.error(await messageFromBlobError(err, 'Failed to export this server'));
      },
    });
  }

  protected cancel(): void {
    this.ref.close(null);
  }

  protected async goToServers(): Promise<void> {
    this.ref.close(null);
    await this.router.navigateByUrl('/servers');
  }

  private extension(format: number): string {
    return format === PACK_FORMAT.modrinth ? 'mrpack' : 'zip';
  }
}

@Injectable({ providedIn: 'root' })
export class ExportPackDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(context: ExportPackDialogContext): Promise<ExportPackResult | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(ExportPackDialog, { context, contentClass: 'sm:max-w-lg' });
      ref.closed$.subscribe((result) => resolve((result as ExportPackResult | null) ?? null));
    });
  }
}
