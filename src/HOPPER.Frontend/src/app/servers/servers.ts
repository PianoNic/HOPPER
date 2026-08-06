import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import {
  lucideDownload,
  lucideKeyRound,
  lucidePencil,
  lucidePlus,
  lucideRefreshCw,
  lucideSearch,
  lucideServer,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { messageFrom, toNumber } from '../shared/utils/format';
import { downloadBlob, messageFromBlobError } from '../shared/utils/download';
import { ServersService } from '../api/api/servers.service';
import { ServerDto } from '../api/model/serverDto';
import { ServerDialogService } from './server-dialog';

@Component({
  selector: 'app-servers',
  imports: [
    ContentHeader,
    DatePipe,
    NgIcon,
    HlmButtonImports,
    HlmInputImports,
    HlmTableImports,
  ],
  providers: [
    provideIcons({
      lucideDownload,
      lucideKeyRound,
      lucidePencil,
      lucidePlus,
      lucideRefreshCw,
      lucideSearch,
      lucideServer,
      lucideTrash2,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">
          Servers
          <span class="text-muted-foreground font-normal">{{ summary() }}</span>
        </h2>
        <div class="flex items-center gap-2">
          <div class="relative">
            <ng-icon
              name="lucideSearch"
              size="14"
              class="text-muted-foreground absolute left-2 top-1/2 -translate-y-1/2"
            />
            <input
              hlmInput
              placeholder="Filter…"
              class="h-8 w-56 pl-7 text-xs"
              [value]="filter()"
              (input)="onFilter($event)"
            />
          </div>
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            (click)="reload()"
            [disabled]="loading()"
          >
            <ng-icon name="lucideRefreshCw" size="14" />
            {{ loading() ? 'Loading…' : 'Refresh' }}
          </button>
          <button hlmBtn size="sm" type="button" (click)="create()">
            <ng-icon name="lucidePlus" size="14" />
            New server
          </button>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto px-4">
        @if (filteredServers().length === 0 && !loading()) {
          @if (servers().length === 0) {
            <div
              class="text-muted-foreground flex h-full flex-col items-center justify-center gap-2 p-10 text-center text-sm"
            >
              <ng-icon name="lucideServer" size="28" class="opacity-60" />
              <p>No servers yet.</p>
              <p class="max-w-md text-xs">
                Press <strong>New server</strong> to create one. Each server gets its own mod list
                and its own client token, and hands out a jar that already knows both - a player
                drops it in <code class="font-mono">mods/</code> and configures nothing.
              </p>
            </div>
          } @else {
            <p class="text-muted-foreground p-4 text-sm">No servers match "{{ filter() }}".</p>
          }
        } @else {
          <table hlmTable>
            <thead hlmTableHeader>
              <tr hlmTableRow>
                <th hlmTableHead>Name</th>
                <th hlmTableHead>Slug</th>
                <th hlmTableHead class="text-right">Mods</th>
                <th hlmTableHead class="text-right">Clients</th>
                <th hlmTableHead>Created</th>
                <th hlmTableHead class="text-right">Actions</th>
              </tr>
            </thead>
            <tbody hlmTableBody>
              @for (s of filteredServers(); track s.id) {
                <tr hlmTableRow class="cursor-pointer" (click)="open(s)">
                  <td hlmTableCell class="font-medium">{{ s.name }}</td>
                  <td hlmTableCell class="font-mono text-xs">{{ s.slug }}</td>
                  <td hlmTableCell class="text-right font-mono text-xs">{{ count(s.modCount) }}</td>
                  <td hlmTableCell class="text-right font-mono text-xs">
                    {{ count(s.clientCount) }}
                  </td>
                  <td hlmTableCell class="font-mono text-xs">
                    {{ s.createdAt | date: 'yyyy-MM-dd HH:mm' }}
                  </td>
                  <td hlmTableCell class="text-right">
                    <span class="inline-flex items-center justify-end gap-0.5">
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        type="button"
                        title="Copy client token"
                        [disabled]="busy()[s.id]"
                        (click)="copyToken(s, $event)"
                      >
                        <ng-icon name="lucideKeyRound" size="14" />
                      </button>
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        type="button"
                        title="Download client jar"
                        [disabled]="busy()[s.id]"
                        (click)="downloadJar(s, $event)"
                      >
                        <ng-icon name="lucideDownload" size="14" />
                      </button>
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        type="button"
                        title="Rename server"
                        [disabled]="busy()[s.id]"
                        (click)="rename(s, $event)"
                      >
                        <ng-icon name="lucidePencil" size="14" />
                      </button>
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        type="button"
                        title="Delete server"
                        [disabled]="busy()[s.id]"
                        (click)="remove(s, $event)"
                      >
                        <ng-icon name="lucideTrash2" size="14" />
                      </button>
                    </span>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </section>
  `,
})
export class Servers {
  private readonly api = inject(ServersService);
  private readonly serverDialog = inject(ServerDialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);

  protected readonly servers = signal<ReadonlyArray<ServerDto>>([]);
  protected readonly loading = signal(false);
  protected readonly filter = signal('');

  // Keyed by server id rather than a single flag: the row actions are per row, and one server's
  // jar being built must not grey out every other row's buttons.
  protected readonly busy = signal<Record<string, boolean>>({});

  protected readonly filteredServers = computed(() => {
    const q = this.filter().trim().toLowerCase();
    if (q === '') return this.servers();
    return this.servers().filter(
      (s) => s.name.toLowerCase().includes(q) || s.slug.toLowerCase().includes(q),
    );
  });

  protected readonly summary = computed(() => {
    const list = this.servers();
    if (list.length === 0) return '';
    const mods = list.reduce((sum, s) => sum + toNumber(s.modCount), 0);
    return `· ${list.length} server${list.length === 1 ? '' : 's'} · ${mods} jar${mods === 1 ? '' : 's'}`;
  });

  constructor() {
    this.reload();
  }

  protected count(value: unknown): number {
    return toNumber(value);
  }

  protected onFilter(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }

  protected reload(): void {
    this.loading.set(true);
    this.api.apiServersGet().subscribe({
      next: (servers) => {
        this.servers.set(servers);
        this.loading.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load servers'));
        this.loading.set(false);
      },
    });
  }

  protected open(server: ServerDto): void {
    this.router.navigate(['/server', server.id]);
  }

  protected async create(): Promise<void> {
    const created = await this.serverDialog.open({ mode: 'create' });
    if (!created) return;
    this.reload();
    // Straight into the new server: an empty server is not a destination, it is a prompt to add
    // mods, and its own pages are where that happens.
    this.router.navigate(['/server', created.id]);
  }

  protected async rename(server: ServerDto, event: Event): Promise<void> {
    event.stopPropagation();
    const saved = await this.serverDialog.open({ mode: 'rename', server });
    if (saved) this.reload();
  }

  protected copyToken(server: ServerDto, event: Event): void {
    event.stopPropagation();
    this.setBusy(server.id, true);

    // Fetched on the click, never on page load: the list endpoint deliberately omits the token so
    // that a credential opening a whole mod set is not sitting in every dashboard response.
    this.api.apiServersIdTokenGet(server.id).subscribe({
      next: async (result) => {
        this.setBusy(server.id, false);
        try {
          await navigator.clipboard.writeText(result.token);
          toast.success(`Client token for ${server.name} copied.`);
        } catch {
          // The clipboard API is unavailable over plain http on some browsers, and a token the
          // admin cannot see anywhere is worse than one shown in a toast they dismissed.
          toast.error(`Could not reach the clipboard. The token is ${result.token}`);
        }
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to read the client token'));
        this.setBusy(server.id, false);
      },
    });
  }

  protected downloadJar(server: ServerDto, event: Event): void {
    event.stopPropagation();
    this.setBusy(server.id, true);

    // FileResult is what the generator infers from the action's declared return type, but the
    // request runs with responseType 'blob' because application/java-archive is neither text nor
    // JSON - so the value on the wire is a Blob and the declared type is the fiction here.
    this.api.apiServersIdJarGet(server.id).subscribe({
      next: (jar) => {
        downloadBlob(jar as unknown as Blob, `${server.slug}-hopper.jar`);
        this.setBusy(server.id, false);
      },
      error: async (err) => {
        toast.error(await messageFromBlobError(err, 'Failed to build the client jar'));
        this.setBusy(server.id, false);
      },
    });
  }

  protected async remove(server: ServerDto, event: Event): Promise<void> {
    event.stopPropagation();
    const ok = await this.confirm.open({
      title: `Delete ${server.name}?`,
      message:
        'Its mods, its clients and its token go with it, and every jar already handed out for it stops working. Jars it shares with another server are kept.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;

    this.setBusy(server.id, true);
    this.api.apiServersIdDelete(server.id).subscribe({
      next: () => {
        this.setBusy(server.id, false);
        this.reload();
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to delete the server'));
        this.setBusy(server.id, false);
      },
    });
  }

  private setBusy(id: string, value: boolean): void {
    this.busy.update((b) => ({ ...b, [id]: value }));
  }
}
