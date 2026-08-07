import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, debounceTime, distinctUntilChanged, EMPTY, forkJoin, switchMap } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import {
  lucideCheck,
  lucideDownload,
  lucideExternalLink,
  lucidePackage,
  lucideSearch,
  lucideSettings,
  lucideTriangleAlert,
} from '@ng-icons/lucide';
import { simpleModrinth } from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ModrinthService } from '../api/api/modrinth.service';
import { ServersService } from '../api/api/servers.service';
import { ModrinthGameVersionDto } from '../api/model/modrinthGameVersionDto';
import { ModrinthSearchHitDto } from '../api/model/modrinthSearchHitDto';
import { ServerDto } from '../api/model/serverDto';
import { ApiModrinthSearchGetLimitParameter } from '../api/model/apiModrinthSearchGetLimitParameter';
import { ApiModrinthSearchGetOffsetParameter } from '../api/model/apiModrinthSearchGetOffsetParameter';
import { apiNumber, messageFrom, toNumber } from '../shared/utils/format';
import { serverIdSignal } from './server-route';
import {
  MOD_LOADER,
  SEARCH_INDEX,
  formatCount,
  modLoaderFacet,
  modrinthProjectUrl,
} from './mod-labels';
import { ModrinthVersionDialogService } from './modrinth-version-dialog';
import { ModrinthPlanDialogService } from './modrinth-plan-dialog';

const PAGE_SIZE = 20;

const SEARCH_DEBOUNCE_MS = 350;

const SORTS: ReadonlyArray<{ value: number; label: string }> = [
  { value: SEARCH_INDEX.relevance, label: 'Relevance' },
  { value: SEARCH_INDEX.downloads, label: 'Downloads' },
  { value: SEARCH_INDEX.follows, label: 'Followers' },
  { value: SEARCH_INDEX.newest, label: 'Newest' },
  { value: SEARCH_INDEX.updated, label: 'Updated' },
];

type SearchKey = {
  serverId: string;
  query: string;
  loader: string;
  gameVersion: string;
  index: number;
  offset: number;
  tick: number;
};

@Component({
  selector: 'app-server-browse',
  imports: [
    ContentHeader,
    NgIcon,
    RouterLink,
    HlmBadgeImports,
    HlmButtonImports,
    HlmInputImports,
    HlmSelectImports,
  ],
  providers: [
    provideIcons({
      simpleModrinth,
      lucideCheck,
      lucideDownload,
      lucideExternalLink,
      lucidePackage,
      lucideSearch,
      lucideSettings,
      lucideTriangleAlert,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">{{ serverName() }}</span>
    </app-content-header>

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex flex-wrap items-center justify-between gap-2 border-b py-2">
        <h2 class="flex items-center gap-1.5 text-sm font-medium">
          <ng-icon name="simpleModrinth" size="14" />
          Browse Modrinth
          <span class="text-muted-foreground font-normal">{{ summary() }}</span>
        </h2>

        @if (platformReady()) {
          <div class="flex flex-wrap items-center gap-2">
            <div class="relative">
              <ng-icon
                name="lucideSearch"
                size="14"
                class="text-muted-foreground absolute left-2 top-1/2 -translate-y-1/2"
              />
              <input
                hlmInput
                placeholder="Search mods…"
                class="h-8 w-64 pl-7 text-xs"
                [value]="term()"
                (input)="onTerm($event)"
              />
            </div>

            <hlm-select [value]="gameVersion()" (valueChange)="onGameVersion($event)">
              <hlm-select-trigger size="sm" class="w-32 text-xs">
                <hlm-select-value placeholder="Version" />
              </hlm-select-trigger>
              <ng-template hlmSelectPortal>
                <hlm-select-content>
                  @for (v of gameVersions(); track v.version) {
                    <hlm-select-item [value]="v.version">{{ v.version }}</hlm-select-item>
                  }
                </hlm-select-content>
              </ng-template>
            </hlm-select>

            <hlm-select [value]="loader()" (valueChange)="onLoader($event)">
              <hlm-select-trigger size="sm" class="w-32 text-xs">
                <hlm-select-value placeholder="Loader" />
              </hlm-select-trigger>
              <ng-template hlmSelectPortal>
                <hlm-select-content>
                  @for (l of loaders(); track l) {
                    <hlm-select-item [value]="l">{{ l }}</hlm-select-item>
                  }
                </hlm-select-content>
              </ng-template>
            </hlm-select>

            <hlm-select [value]="sortValue()" (valueChange)="onSort($event)">
              <hlm-select-trigger size="sm" class="w-32 text-xs">
                <hlm-select-value placeholder="Sort" />
              </hlm-select-trigger>
              <ng-template hlmSelectPortal>
                <hlm-select-content>
                  @for (s of sorts; track s.value) {
                    <hlm-select-item [value]="s.label">{{ s.label }}</hlm-select-item>
                  }
                </hlm-select-content>
              </ng-template>
            </hlm-select>
          </div>
        }
      </header>

      <div class="min-h-0 flex-1 overflow-auto px-4">
        @if (!platformReady()) {
          <!-- A state, not an error. The server has never been told what it runs, so there is no
               honest filter to apply: searching every loader at once would offer jars this server
               cannot load. -->
          <div
            class="text-muted-foreground flex h-full flex-col items-center justify-center gap-2 p-10 text-center text-sm"
          >
            <ng-icon name="lucideTriangleAlert" size="28" class="opacity-60" />
            <p>This server has no Minecraft version or loader set.</p>
            <p class="max-w-md text-xs">
              The browser filters by both, so it needs them before it can show anything worth
              installing. Set them on the Servers page and come back.
            </p>
            <a hlmBtn variant="outline" size="sm" routerLink="/servers">
              <ng-icon name="lucideSettings" size="14" />
              Go to Servers
            </a>
          </div>
        } @else if (hits().length === 0 && !loading()) {
          <div
            class="text-muted-foreground flex h-full flex-col items-center justify-center gap-2 p-10 text-center text-sm"
          >
            <ng-icon name="lucidePackage" size="28" class="opacity-60" />
            @if (term().trim() === '') {
              <p>No {{ loader() }} mods listed for Minecraft {{ gameVersion() }}.</p>
            } @else {
              <p>Nothing matches "{{ term() }}" for {{ loader() }} {{ gameVersion() }}.</p>
            }
            <p class="max-w-md text-xs">
              Try a different Minecraft version or loader - most mods are published for one line at
              a time.
            </p>
          </div>
        } @else {
          <ul class="flex flex-col gap-2 py-3">
            @for (h of hits(); track h.projectId) {
              <li class="hover:bg-accent/40 flex items-start gap-3 rounded-md border p-3">
                @if (h.iconUrl) {
                  <img
                    [src]="h.iconUrl"
                    [alt]="h.title"
                    loading="lazy"
                    class="size-11 shrink-0 rounded-md object-cover"
                  />
                } @else {
                  <div
                    class="bg-muted text-muted-foreground flex size-11 shrink-0 items-center justify-center rounded-md"
                  >
                    <ng-icon name="lucidePackage" size="18" />
                  </div>
                }

                <div class="flex min-w-0 flex-1 flex-col gap-1">
                  <div class="flex flex-wrap items-center gap-2">
                    <span class="truncate text-sm font-medium">{{ h.title }}</span>
                    @if (h.author) {
                      <span class="text-muted-foreground text-xs">by {{ h.author }}</span>
                    }
                  </div>
                  @if (h.description) {
                    <p class="text-muted-foreground line-clamp-2 text-xs">{{ h.description }}</p>
                  }
                  <div class="text-muted-foreground flex flex-wrap items-center gap-2 text-xs">
                    <span class="inline-flex items-center gap-1">
                      <ng-icon name="lucideDownload" size="12" />
                      {{ downloads(h) }}
                    </span>
                    @for (c of categories(h); track c) {
                      <span hlmBadge variant="outline" class="text-xs">{{ c }}</span>
                    }
                  </div>
                </div>

                <div class="flex shrink-0 items-center gap-1">
                  @if (projectUrl(h); as url) {
                    <a
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      title="Open on Modrinth"
                      [href]="url"
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      <ng-icon name="lucideExternalLink" size="14" />
                    </a>
                  }
                  <button hlmBtn variant="outline" size="sm" type="button" (click)="versions(h)">
                    Versions
                  </button>
                  @if (h.installed) {
                    <button hlmBtn variant="outline" size="sm" type="button" disabled>
                      <ng-icon name="lucideCheck" size="14" />
                      Added
                    </button>
                  } @else {
                    <button
                      hlmBtn
                      size="sm"
                      type="button"
                      [disabled]="picking() === h.projectId"
                      (click)="addLatest(h)"
                    >
                      {{ picking() === h.projectId ? 'Resolving…' : 'Add' }}
                    </button>
                  }
                </div>
              </li>
            }
          </ul>

          <div class="flex justify-center pb-6">
            @if (loading()) {
              <p class="text-muted-foreground text-xs">Loading…</p>
            } @else if (hasMore()) {
              <button hlmBtn variant="outline" size="sm" type="button" (click)="loadMore()">
                Load more
              </button>
            } @else {
              <p class="text-muted-foreground text-xs">End of results.</p>
            }
          </div>
        }
      </div>
    </section>
  `,
})
export class ServerBrowse {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ModrinthService);
  private readonly serversApi = inject(ServersService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly versionDialog = inject(ModrinthVersionDialogService);
  private readonly planDialog = inject(ModrinthPlanDialogService);

  protected readonly serverId = serverIdSignal(this.route);
  protected readonly sorts = SORTS;

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly gameVersions = signal<ReadonlyArray<ModrinthGameVersionDto>>([]);
  protected readonly loaders = signal<ReadonlyArray<string>>([]);
  protected readonly hits = signal<ReadonlyArray<ModrinthSearchHitDto>>([]);
  protected readonly totalHits = signal(0);
  protected readonly loading = signal(false);
  protected readonly picking = signal<string | null>(null);

  protected readonly term = signal('');
  protected readonly loader = signal('');
  protected readonly gameVersion = signal('');
  protected readonly index = signal<number>(SEARCH_INDEX.relevance);
  protected readonly offset = signal(0);

  private readonly reloadTick = signal(0);

  protected readonly serverName = computed(() => this.server()?.name ?? '');

  protected readonly platformReady = computed(
    () => this.loader() !== '' && this.gameVersion() !== '',
  );

  protected readonly hasMore = computed(() => this.hits().length < this.totalHits());

  protected readonly sortValue = computed(
    () => SORTS.find((s) => s.value === this.index())?.label ?? 'Relevance',
  );

  protected readonly summary = computed(() => {
    if (!this.platformReady()) return '';
    const total = this.totalHits();
    if (total === 0) return `· ${this.loader()} ${this.gameVersion()}`;
    return `· ${total} result${total === 1 ? '' : 's'} for ${this.loader()} ${this.gameVersion()}`;
  });

  private readonly searchKey = computed<SearchKey | null>(() => {
    const serverId = this.serverId();
    if (serverId === '' || !this.platformReady()) return null;

    return {
      serverId,
      query: this.term().trim(),
      loader: this.loader(),
      gameVersion: this.gameVersion(),
      index: this.index(),
      offset: this.offset(),
      tick: this.reloadTick(),
    };
  });

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id !== '') this.load(id);
    });

    toObservable(this.searchKey)
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
        switchMap((key) => {
          if (key === null) return EMPTY;
          this.loading.set(true);
          return this.api
            .apiModrinthSearchGet(
              key.query === '' ? undefined : key.query,
              key.loader,
              key.gameVersion,
              key.index,
              apiNumber<ApiModrinthSearchGetOffsetParameter>(key.offset),
              apiNumber<ApiModrinthSearchGetLimitParameter>(PAGE_SIZE),
              key.serverId,
            )
            .pipe(
              catchError((err: unknown) => {
                toast.error(messageFrom(err, 'Failed to search Modrinth'));
                this.loading.set(false);
                return EMPTY;
              }),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.hits.update((current) =>
          toNumber(result.offset) === 0 ? result.hits : [...current, ...result.hits],
        );
        this.totalHits.set(toNumber(result.totalHits));
        this.loading.set(false);
      });
  }

  protected downloads(hit: ModrinthSearchHitDto): string {
    return formatCount(toNumber(hit.downloads));
  }

  protected categories(hit: ModrinthSearchHitDto): ReadonlyArray<string> {
    const loaderNames = new Set(this.loaders().map((l) => l.toLowerCase()));
    return hit.categories.filter((c) => !loaderNames.has(c.toLowerCase())).slice(0, 4);
  }

  protected projectUrl(hit: ModrinthSearchHitDto): string | null {
    return modrinthProjectUrl(hit.slug);
  }

  protected onTerm(event: Event): void {
    this.term.set((event.target as HTMLInputElement).value);
    this.resetPaging();
  }

  protected onLoader(value: unknown): void {
    if (typeof value !== 'string' || value === '') return;
    this.loader.set(value);
    this.resetPaging();
  }

  protected onGameVersion(value: unknown): void {
    if (typeof value !== 'string' || value === '') return;
    this.gameVersion.set(value);
    this.resetPaging();
  }

  protected onSort(value: unknown): void {
    const match = SORTS.find((s) => s.label === value);
    if (!match) return;
    this.index.set(match.value);
    this.resetPaging();
  }

  protected loadMore(): void {
    if (this.loading() || !this.hasMore()) return;
    this.offset.set(this.hits().length);
  }

  protected async versions(hit: ModrinthSearchHitDto): Promise<void> {
    const pick = await this.versionDialog.open({
      serverId: this.serverId(),
      projectId: hit.projectId,
      projectTitle: hit.title,
      slug: hit.slug ?? null,
      loader: this.loader(),
      gameVersion: this.gameVersion(),
    });
    if (!pick) return;
    await this.plan(pick.versionId, pick.title);
  }

  protected addLatest(hit: ModrinthSearchHitDto): void {
    if (hit.installed) return;
    if (this.picking() !== null) return;
    this.picking.set(hit.projectId);

    this.api
      .apiModrinthProjectsIdOrSlugVersionsGet(
        hit.projectId,
        this.loader(),
        this.gameVersion(),
        this.serverId(),
      )
      .subscribe({
        next: async (versions) => {
          this.picking.set(null);

          const newest = versions.find((v) => (v.fileName ?? '') !== '');
          if (!newest) {
            toast.error(
              `${hit.title} has no version built for ${this.loader()} ${this.gameVersion()}`,
            );
            return;
          }
          await this.plan(newest.id, hit.title);
        },
        error: (err) => {
          this.picking.set(null);
          toast.error(messageFrom(err, 'Failed to load the versions of this mod'));
        },
      });
  }

  private async plan(versionId: string, title: string): Promise<void> {
    const result = await this.planDialog.open({
      serverId: this.serverId(),
      rootVersionIds: [versionId],
      rootTitles: [title],
    });
    if (!result) return;

    const added = result.installed.length + result.adopted.length + result.replaced.length;
    if (added > 0) toast.success(`Added ${added} mod${added === 1 ? '' : 's'} to this server`);

    this.refreshHits();
  }

  private resetPaging(): void {
    this.offset.set(0);
  }

  private refreshHits(): void {
    this.offset.set(0);
    this.reloadTick.update((t) => t + 1);
  }

  private load(id: string): void {
    this.loading.set(true);
    forkJoin({
      server: this.serversApi.apiServersIdGet(id),
      tags: this.api.apiModrinthTagsGet(),
    }).subscribe({
      next: ({ server, tags }) => {
        this.server.set(server);
        this.gameVersions.set(tags.gameVersions);

        const known = new Set(
          [MOD_LOADER.forge, MOD_LOADER.neoForge, MOD_LOADER.fabric, MOD_LOADER.quilt].map((l) =>
            modLoaderFacet(l),
          ),
        );
        this.loaders.set(tags.loaders.filter((l) => known.has(l)));

        this.loader.set(modLoaderFacet(server.loader) ?? '');
        this.gameVersion.set(server.minecraftVersion ?? '');
        this.resetPaging();
        this.loading.set(false);

        if (!this.platformReady()) {
          this.hits.set([]);
          this.totalHits.set(0);
        }
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load this server'));
        this.loading.set(false);
      },
    });
  }
}
