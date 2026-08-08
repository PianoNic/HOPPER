import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  Injectable,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, switchMap } from 'rxjs';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
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
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage } from '@ng-icons/lucide';
import { ServerIcon } from '../shared/components/server-icon/server-icon';
import { ModrinthService } from '../api/api/modrinth.service';
import { ServersService } from '../api/api/servers.service';
import { ModrinthGameVersionDto } from '../api/model/modrinthGameVersionDto';
import { LoaderVersionDto } from '../api/model/loaderVersionDto';
import { LoadersService } from '../api/api/loaders.service';
import { ServerDto } from '../api/model/serverDto';
import { messageFrom } from '../shared/utils/format';
import { modLoaderLabel } from './mod-labels';
import { ModLoader } from '../api/model/modLoader';

export type ServerDialogContext = { mode: 'create' } | { mode: 'rename'; server: ServerDto };

const REAL_LOADERS: ReadonlyArray<ModLoader> = [
  ModLoader.Forge,
  ModLoader.NeoForge,
  ModLoader.Fabric,
  ModLoader.Quilt,
];

const LOADERS: ReadonlyArray<{ value: ModLoader; label: string }> = REAL_LOADERS.map((value) => ({
  value,
  label: modLoaderLabel(value),
}));

const LOADERS_WITH_UNSET: ReadonlyArray<{ value: ModLoader; label: string }> = [
  { value: ModLoader.Unknown, label: modLoaderLabel(ModLoader.Unknown) },
  ...LOADERS,
];

@Component({
  selector: 'app-server-dialog',
  imports: [
    HlmButtonImports,
    ButtonLoading,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    NgIcon,
    ServerIcon,
  ],
  providers: [provideIcons({ lucideImage })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ creating ? 'New server' : 'Edit server' }}</h3>
      <p hlmDialogDescription>
        @if (creating) {
          A server owns its own mod list, its own clients and its own client token. Jars shared with
          another server are stored once, so a second server costs nothing in disk.
        } @else {
          Tokens, mods and the slug are untouched. The slug is what names the generated jar, so it
          stays put even when the server is renamed.
        }
      </p>
    </hlm-dialog-header>
    <div class="flex flex-col gap-3">
      <div class="flex items-center gap-3">
        @if (iconPreview(); as preview) {
          <img
            [src]="preview"
            alt="The icon about to be uploaded"
            width="48"
            height="48"
            class="shrink-0 rounded object-cover"
          />
        } @else {
          <app-server-icon [sha256]="currentIcon()" [name]="name()" [size]="48" />
        }

        <input #iconPicker type="file" class="hidden" accept="image/*" (change)="pickIcon($event)" />

        <div class="flex flex-wrap items-center gap-2">
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            [disabled]="saving()"
            (click)="iconPicker.click()"
          >
            <ng-icon name="lucideImage" size="14" />
            {{ hasIcon() ? 'Replace icon' : 'Add an icon' }}
          </button>

          @if (hasIcon()) {
            <button
              hlmBtn
              variant="ghost"
              size="sm"
              type="button"
              class="text-destructive hover:text-destructive"
              [disabled]="saving()"
              (click)="dropIcon()"
            >
              Remove
            </button>
          }
        </div>
      </div>

      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="server-name">Name</label>
        <input
          hlmInput
          id="server-name"
          class="w-full"
          placeholder="Survival 1.20.1"
          [value]="name()"
          [disabled]="saving()"
          (input)="onName($event)"
        />
      </div>
      <div class="grid grid-cols-2 gap-3">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="server-mc">Minecraft version</label>
          <hlm-select
            id="server-mc"
            [value]="minecraftVersion()"
            (valueChange)="onMinecraftVersion($event)"
          >
            <hlm-select-trigger class="w-full">
              <hlm-select-value placeholder="Not set" />
            </hlm-select-trigger>
            <ng-template hlmSelectPortal>
              <hlm-select-content>
                @for (v of gameVersions(); track v.version) {
                  <hlm-select-item [value]="v.version">{{ v.version }}</hlm-select-item>
                }
              </hlm-select-content>
            </ng-template>
          </hlm-select>
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="server-loader">Loader</label>
          <hlm-select id="server-loader" [value]="loaderLabel()" (valueChange)="onLoader($event)">
            <hlm-select-trigger class="w-full">
              <hlm-select-value placeholder="Not set" />
            </hlm-select-trigger>
            <ng-template hlmSelectPortal>
              <hlm-select-content>
                @for (l of loaders; track l.value) {
                  <hlm-select-item [value]="l.label">{{ l.label }}</hlm-select-item>
                }
              </hlm-select-content>
            </ng-template>
          </hlm-select>
        </div>
      </div>
      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="server-loader-version">Loader version</label>
        @if (loaderVersions().length > 0) {
          <hlm-select
            id="server-loader-version"
            [value]="loaderVersion()"
            (valueChange)="onLoaderVersionPicked($event)"
          >
            <hlm-select-trigger class="w-full font-mono">
              <hlm-select-value placeholder="Not set" />
            </hlm-select-trigger>
            <ng-template hlmSelectPortal>
              <hlm-select-content>
                @for (v of loaderVersions(); track v.version) {
                  <hlm-select-item [value]="v.version" class="font-mono">
                    {{ v.version }}
                    @if (v.recommended) {
                      <span class="text-muted-foreground ml-2 font-sans text-xs">recommended</span>
                    }
                  </hlm-select-item>
                }
              </hlm-select-content>
            </ng-template>
          </hlm-select>
        } @else {
          <input
            hlmInput
            id="server-loader-version"
            class="w-full font-mono"
            placeholder="47.4.10"
            [value]="loaderVersion()"
            [disabled]="saving()"
            (input)="onLoaderVersion($event)"
          />
          <p class="text-muted-foreground text-xs">
            The loader's own version, with no Minecraft prefix -
            <code class="font-mono">47.4.10</code>, not
            <code class="font-mono">1.20.1-47.4.10</code>.
          </p>
        }
      </div>
    </div>
    <div class="flex justify-end gap-2">
      <button hlmBtn variant="ghost" type="button" [disabled]="saving()" (click)="cancel()">
        Cancel
      </button>
      <button hlmBtn type="button" [disabled]="saving() || !canSave()" (click)="save()" [loading]="saving()">
        {{ saving() ? 'Saving' : creating ? 'Create' : 'Save' }}
      </button>
    </div>
  `,
})
export class ServerDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(ServersService);
  private readonly modrinth = inject(ModrinthService);
  private readonly loaders$ = inject(LoadersService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ctx = injectBrnDialogContext<ServerDialogContext>();

  protected readonly creating = this.ctx.mode === 'create';
  protected readonly loaders = this.creating ? LOADERS : LOADERS_WITH_UNSET;

  protected readonly name = signal(this.ctx.mode === 'rename' ? this.ctx.server.name : '');
  protected readonly saving = signal(false);

  private readonly picked = signal<File | null>(null);
  private readonly removing = signal(false);

  protected readonly iconPreview = signal<string | null>(null);

  protected readonly currentIcon = computed(() =>
    this.removing() ? null : (this.ctx.mode === 'create' ? null : this.ctx.server.iconSha256) ?? null,
  );

  protected readonly hasIcon = computed(() => this.iconPreview() !== null || this.currentIcon() !== null);

  protected readonly minecraftVersion = signal(
    this.ctx.mode === 'rename' ? (this.ctx.server.minecraftVersion ?? '') : '',
  );
  protected readonly loader = signal(
    this.ctx.mode === 'rename' ? this.ctx.server.loader : ModLoader.Forge,
  );
  protected readonly loaderVersion = signal(
    this.ctx.mode === 'rename' ? (this.ctx.server.loaderVersion ?? '') : '',
  );

  protected readonly gameVersions = signal<ReadonlyArray<ModrinthGameVersionDto>>([]);
  protected readonly loaderVersions = signal<ReadonlyArray<LoaderVersionDto>>([]);

  protected readonly loaderLabel = computed(() => modLoaderLabel(this.loader()));

  constructor() {
    this.destroyRef.onDestroy(() => this.revokePreview());

    this.modrinth.apiModrinthTagsGet().subscribe({
      next: (tags) => {
        this.gameVersions.set(tags.gameVersions);

        if (this.creating && this.minecraftVersion() === '') {
          const newest = tags.gameVersions[0]?.version;
          if (newest) this.minecraftVersion.set(newest);
        }
      },
      error: () => this.gameVersions.set([]),
    });

    toObservable(computed(() => ({ loader: this.loader(), minecraft: this.minecraftVersion() })))
      .pipe(
        switchMap(({ loader, minecraft }) => {
          this.loaderVersions.set([]);
          if (loader === ModLoader.Unknown) return EMPTY;

          return this.loaders$.apiLoadersLoaderVersionsGet(loader, minecraft || undefined).pipe(
            catchError((err: unknown) => {
              toast.error(messageFrom(err, 'Could not load the list of loader builds'));
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((versions) => {
        this.loaderVersions.set(versions);

        const current = this.loaderVersion();
        if (current !== '' && versions.some((v) => v.version === current)) return;

        const recommended = versions.find((v) => v.recommended)?.version ?? versions[0]?.version;
        this.loaderVersion.set(recommended ?? '');
      });
  }

  protected readonly canSave = computed(() => this.name().trim() !== '');

  protected onName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected onMinecraftVersion(value: unknown): void {
    if (typeof value === 'string') this.minecraftVersion.set(value);
  }

  protected onLoader(value: unknown): void {
    const match = this.loaders.find((l) => l.label === value);
    if (match) this.loader.set(match.value);
  }

  protected onLoaderVersionPicked(value: unknown): void {
    if (typeof value === 'string') this.loaderVersion.set(value);
  }

  protected onLoaderVersion(event: Event): void {
    this.loaderVersion.set((event.target as HTMLInputElement).value);
  }

  protected pickIcon(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;

    input.value = '';
    if (!file) return;

    this.revokePreview();
    this.picked.set(file);
    this.iconPreview.set(URL.createObjectURL(file));
    this.removing.set(false);
  }

  protected dropIcon(): void {
    this.revokePreview();
    this.picked.set(null);
    this.iconPreview.set(null);

    // Only meaningful on edit; on create there is nothing stored to remove yet.
    this.removing.set(this.ctx.mode !== 'create');
  }

  private revokePreview(): void {
    const url = this.iconPreview();
    if (url) URL.revokeObjectURL(url);
  }

  // The server has to exist before its icon can be posted, so on create this runs after the POST.
  // A saved server with a failed icon is reported and kept: losing the server over its picture
  // would be the worse trade, and the icon can be set again from Setup.
  private finishIcon(server: ServerDto): void {
    const picked = this.picked();

    if (picked) {
      this.api.apiServersIdIconPost(server.id, picked).subscribe({
        next: (result) => this.ref.close({ ...server, iconSha256: result.iconSha256 }),
        error: (err) => {
          toast.error(messageFrom(err, 'The server was saved, but that icon could not be read'));
          this.ref.close(server);
        },
      });
      return;
    }

    if (this.removing()) {
      this.api.apiServersIdIconDelete(server.id).subscribe({
        next: () => this.ref.close({ ...server, iconSha256: null }),
        error: (err) => {
          toast.error(messageFrom(err, 'The server was saved, but its icon could not be removed'));
          this.ref.close(server);
        },
      });
      return;
    }

    this.ref.close(server);
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);

    const name = this.name().trim();

    const minecraftVersion = this.minecraftVersion().trim() || null;
    const loaderVersion = this.loaderVersion().trim() || null;
    const loader = this.loader();

    const request$ =
      this.ctx.mode === 'create'
        ? this.api.apiServersPost({
            name,
            minecraftVersion,
            loader,
            loaderVersion,
          })
        : this.api.apiServersIdPut(this.ctx.server.id, {
            name,
            minecraftVersion,
            loader,
            loaderVersion,
          });

    request$.subscribe({
      next: (server) => this.finishIcon(server),
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to save the server'));
        this.saving.set(false);
      },
    });
  }

  protected cancel(): void {
    this.ref.close(null);
  }
}

@Injectable({ providedIn: 'root' })
export class ServerDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(context: ServerDialogContext): Promise<ServerDto | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(ServerDialog, { context, contentClass: 'sm:max-w-md' });
      ref.closed$.subscribe((result) => resolve((result as ServerDto | null) ?? null));
    });
  }
}
