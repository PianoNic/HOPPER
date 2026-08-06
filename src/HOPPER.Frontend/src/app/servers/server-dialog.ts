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
import { ServersService } from '../api/api/servers.service';
import { ServerDto } from '../api/model/serverDto';
import { messageFrom } from '../shared/utils/format';

/**
 * Create and rename are the same two fields over the same two rules, so they are one component with
 * a mode rather than two that drift apart. The only real difference is that create may leave the
 * slug blank and let the server derive one, while a rename has to state the slug it is keeping -
 * PUT replaces both fields.
 */
export type ServerDialogContext = { mode: 'create' } | { mode: 'rename'; server: ServerDto };

@Component({
  selector: 'app-server-dialog',
  imports: [
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ creating ? 'New server' : 'Rename server' }}</h3>
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
  private readonly ctx = injectBrnDialogContext<ServerDialogContext>();

  protected readonly creating = this.ctx.mode === 'create';

  protected readonly name = signal(this.ctx.mode === 'rename' ? this.ctx.server.name : '');
  protected readonly slug = signal(this.ctx.mode === 'rename' ? this.ctx.server.slug : '');
  protected readonly saving = signal(false);

  // Mirrors the server's own derivation so the placeholder shows what leaving the field empty will
  // actually produce. It is a preview only - the server derives it again and resolves collisions.
  protected readonly slugPlaceholder = computed(() => {
    const derived = this.name()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');
    return derived === '' ? 'survival-1-20-1' : derived;
  });

  // A rename must send a slug, so an emptied field is not a valid rename the way it is a valid
  // create. Everything else the server validates and reports back through a toast.
  protected readonly canSave = computed(
    () => this.name().trim() !== '' && (this.creating || this.slug().trim() !== ''),
  );

  protected onName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected onSlug(event: Event): void {
    this.slug.set((event.target as HTMLInputElement).value);
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);

    const name = this.name().trim();
    const slug = this.slug().trim();

    const request$ =
      this.ctx.mode === 'create'
        ? this.api.apiServersPost({ name, slug: slug === '' ? null : slug })
        : this.api.apiServersIdPut(this.ctx.server.id, { name, slug });

    request$.subscribe({
      next: (server) => this.ref.close(server),
      error: (err) => {
        // 409 on a taken slug and 400 on a malformed one both arrive as { error: "..." }, so the
        // server's own wording is what the admin reads.
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
