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
import { ModrinthService } from '../api/api/modrinth.service';
import { ServersService } from '../api/api/servers.service';
import { ModrinthGameVersionDto } from '../api/model/modrinthGameVersionDto';
import { LoaderVersionDto } from '../api/model/loaderVersionDto';
import { LoadersService } from '../api/api/loaders.service';
import { ServerDto } from '../api/model/serverDto';
import { messageFrom } from '../shared/utils/format';
import { MOD_LOADER, modLoaderLabel } from './mod-labels';

export type ServerDialogContext = { mode: 'create' } | { mode: 'rename'; server: ServerDto };

const REAL_LOADERS: ReadonlyArray<number> = [
  MOD_LOADER.forge,
  MOD_LOADER.neoForge,
  MOD_LOADER.fabric,
  MOD_LOADER.quilt,
];

// "Not set" is offered only when editing. A server without a loader cannot be browsed for, cannot
// hand out a jar and has no version list, so it is not a state worth being able to create.
// An existing one may already be in it, and opening its dialog must not change that by itself.
const LOADERS: ReadonlyArray<{ value: number; label: string }> = REAL_LOADERS.map((value) => ({
  value,
  label: modLoaderLabel(value),
}));

const LOADERS_WITH_UNSET: ReadonlyArray<{ value: number; label: string }> = [
  { value: MOD_LOADER.unknown, label: modLoaderLabel(MOD_LOADER.unknown) },
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
  ],
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

      <!-- What this server runs. All three are optional and leaving them unset changes nothing
           about distributing mods - they exist because the Modrinth browser has to filter on
           something, and because an exported pack names an exact platform. -->
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

        <!-- The dropdown only when the list arrived. An air-gapped deployment, or a build too new
             for the metadata, still has to be enterable, so the text input is the fallback rather
             than a dead field. -->
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

          <!-- Only beside the input. Nobody typing is nobody getting the format wrong, and the
               dropdown's own entries are already in it. -->
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

  protected readonly minecraftVersion = signal(
    this.ctx.mode === 'rename' ? (this.ctx.server.minecraftVersion ?? '') : '',
  );
  protected readonly loader = signal(
    this.ctx.mode === 'rename' ? this.ctx.server.loader : MOD_LOADER.forge,
  );
  protected readonly loaderVersion = signal(
    this.ctx.mode === 'rename' ? (this.ctx.server.loaderVersion ?? '') : '',
  );

  protected readonly gameVersions = signal<ReadonlyArray<ModrinthGameVersionDto>>([]);
  protected readonly loaderVersions = signal<ReadonlyArray<LoaderVersionDto>>([]);

  protected readonly loaderLabel = computed(() => modLoaderLabel(this.loader()));

  constructor() {
    this.modrinth.apiModrinthTagsGet().subscribe({
      next: (tags) => {
        this.gameVersions.set(tags.gameVersions);

        // The first entry, because Modrinth returns newest first and the API already drops
        // everything that is not a release. Not the first `major` one: that flag marks 1.20 apart
        // from 1.20.1 and is false for every current version, so it would find nothing.
        //
        // Only on create, and only while the field is untouched. A failed fetch leaves it empty,
        // which is what it did before, so the dialog still works without Modrinth.
        if (this.creating && this.minecraftVersion() === '') {
          const newest = tags.gameVersions[0]?.version;
          if (newest) this.minecraftVersion.set(newest);
        }
      },
      error: () => this.gameVersions.set([]),
    });

    // Refetched whenever the loader or the Minecraft version moves, because the answer depends on
    // both: a Forge build exists for one Minecraft version and a NeoForge build encodes it.
    toObservable(computed(() => ({ loader: this.loader(), minecraft: this.minecraftVersion() })))
      .pipe(
        switchMap(({ loader, minecraft }) => {
          this.loaderVersions.set([]);
          if (loader === MOD_LOADER.unknown) return EMPTY;

          return this.loaders$.apiLoadersLoaderVersionsGet(loader, minecraft || undefined).pipe(
            catchError((err: unknown) => {
              // A state, not a blocker: the text input takes over and the dialog still saves.
              toast.error(messageFrom(err, 'Could not load the list of loader builds'));
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((versions) => {
        this.loaderVersions.set(versions);

        // Only when nothing has been chosen yet, or when what was chosen no longer exists for this
        // combination. Editing a server must not move a build the admin picked on purpose.
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
      next: (server) => this.ref.close(server),
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
