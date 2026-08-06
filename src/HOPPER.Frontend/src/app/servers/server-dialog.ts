import { ChangeDetectionStrategy, Component, computed, inject, Injectable, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
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
import { ServerDto } from '../api/model/serverDto';
import { messageFrom } from '../shared/utils/format';
import { MOD_LOADER, modLoaderLabel } from './mod-labels';

export type ServerDialogContext = { mode: 'create' } | { mode: 'rename'; server: ServerDto };

const LOADERS: ReadonlyArray<{ value: number; label: string }> = [
  MOD_LOADER.unknown,
  MOD_LOADER.forge,
  MOD_LOADER.neoForge,
  MOD_LOADER.fabric,
  MOD_LOADER.quilt,
].map((value) => ({ value, label: modLoaderLabel(value) }));

@Component({
  selector: 'app-server-dialog',
  imports: [
    HlmButtonImports,
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
          The slug names the generated jar, so changing it changes the filename players download
          next. Tokens and mods are untouched.
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

      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="server-slug">Slug</label>
        <input
          hlmInput
          id="server-slug"
          class="w-full font-mono"
          [placeholder]="slugPlaceholder()"
          [value]="slug()"
          [disabled]="saving()"
          (input)="onSlug($event)"
        />
        <p class="text-muted-foreground text-xs">
          Lowercase letters, digits and dashes. Used as <code class="font-mono">&lt;slug&gt;-hopper.jar</code>.
          @if (creating) {
            Leave it empty to derive one from the name.
          }
        </p>
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
          The loader's own version, with no Minecraft prefix - <code class="font-mono">47.4.10</code
          >, not <code class="font-mono">1.20.1-47.4.10</code>. Each pack format prepends whatever it
          wants.
        </p>
      </div>
    </div>

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="ghost" type="button" [disabled]="saving()" (click)="cancel()">
        Cancel
      </button>
      <button hlmBtn type="button" [disabled]="saving() || !canSave()" (click)="save()">
        {{ saving() ? 'Saving…' : creating ? 'Create' : 'Save' }}
      </button>
    </div>
  `,
})
export class ServerDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(ServersService);
  private readonly modrinth = inject(ModrinthService);
  private readonly ctx = injectBrnDialogContext<ServerDialogContext>();

  protected readonly creating = this.ctx.mode === 'create';
  protected readonly loaders = LOADERS;

  protected readonly name = signal(this.ctx.mode === 'rename' ? this.ctx.server.name : '');
  protected readonly slug = signal(this.ctx.mode === 'rename' ? this.ctx.server.slug : '');
  protected readonly saving = signal(false);

  protected readonly minecraftVersion = signal(
    this.ctx.mode === 'rename' ? (this.ctx.server.minecraftVersion ?? '') : '',
  );
  protected readonly loader = signal(
    this.ctx.mode === 'rename' ? this.ctx.server.loader : MOD_LOADER.unknown,
  );
  protected readonly loaderVersion = signal(
    this.ctx.mode === 'rename' ? (this.ctx.server.loaderVersion ?? '') : '',
  );

  protected readonly gameVersions = signal<ReadonlyArray<ModrinthGameVersionDto>>([]);

  protected readonly loaderLabel = computed(() => modLoaderLabel(this.loader()));

  constructor() {
    this.modrinth.apiModrinthTagsGet().subscribe({
      next: (tags) => this.gameVersions.set(tags.gameVersions),
      error: () => this.gameVersions.set([]),
    });
  }

  protected readonly slugPlaceholder = computed(() => {
    const derived = this.name()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');
    return derived === '' ? 'survival-1-20-1' : derived;
  });

  protected readonly canSave = computed(
    () => this.name().trim() !== '' && (this.creating || this.slug().trim() !== ''),
  );

  protected onName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected onSlug(event: Event): void {
    this.slug.set((event.target as HTMLInputElement).value);
  }

  protected onMinecraftVersion(value: unknown): void {
    if (typeof value === 'string') this.minecraftVersion.set(value);
  }

  protected onLoader(value: unknown): void {
    const match = LOADERS.find((l) => l.label === value);
    if (match) this.loader.set(match.value);
  }

  protected onLoaderVersion(event: Event): void {
    this.loaderVersion.set((event.target as HTMLInputElement).value);
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);

    const name = this.name().trim();
    const slug = this.slug().trim();

    const minecraftVersion = this.minecraftVersion().trim() || null;
    const loaderVersion = this.loaderVersion().trim() || null;
    const loader = this.loader();

    const request$ =
      this.ctx.mode === 'create'
        ? this.api.apiServersPost({
            name,
            slug: slug === '' ? null : slug,
            minecraftVersion,
            loader,
            loaderVersion,
          })
        : this.api.apiServersIdPut(this.ctx.server.id, {
            name,
            slug,
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
