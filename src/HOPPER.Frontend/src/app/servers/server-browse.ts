import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, debounceTime, distinctUntilChanged, EMPTY, forkJoin, switchMap } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import {
  lucideBug,
  lucideDownload,
  lucidePackage,
  lucideSearch,
  lucideSettings,
  lucideTriangleAlert,
  lucideUsers,
  lucideX,
} from '@ng-icons/lucide';
import {
  simpleBitbucket,
  simpleCodeberg,
  simpleGit,
  simpleGitea,
  simpleGithub,
  simpleGitlab,
  simpleModrinth,
  simpleSourcehut,
} from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmResizableImports } from '@spartan-ng/helm/resizable';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ModrinthService } from '../api/api/modrinth.service';
import { ServersService } from '../api/api/servers.service';
import { ModrinthGameVersionDto } from '../api/model/modrinthGameVersionDto';
import { ModrinthProjectDto } from '../api/model/modrinthProjectDto';
import { ModrinthSearchHitDto } from '../api/model/modrinthSearchHitDto';
import { ServerDto } from '../api/model/serverDto';
import { ApiModrinthSearchGetLimitParameter } from '../api/model/apiModrinthSearchGetLimitParameter';
import { ApiModrinthSearchGetOffsetParameter } from '../api/model/apiModrinthSearchGetOffsetParameter';
import { apiNumber, messageFrom, toNumber } from '../shared/utils/format';
import { renderProjectBody } from '../shared/utils/markdown';
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

const PAGE_SIZE = 50;

// How far past the end of the list the next page is fetched, in viewports. Three is enough that a
// steady scroll never reaches the bottom, without pulling the whole catalogue for one glance.
const LOOKAHEAD_VIEWPORTS = 3;

// Matched on the host rather than anywhere in the URL, or a repository called "github-mirror" on
// some other forge would wear the wrong mark.
const SOURCE_HOSTS: ReadonlyArray<{ matches: (url: string) => boolean; icon: string; title: string }> =
  (
    [
      ['github.com', 'simpleGithub', 'Source on GitHub'],
      ['gitlab.com', 'simpleGitlab', 'Source on GitLab'],
      ['codeberg.org', 'simpleCodeberg', 'Source on Codeberg'],
      ['bitbucket.org', 'simpleBitbucket', 'Source on Bitbucket'],
      ['git.sr.ht', 'simpleSourcehut', 'Source on SourceHut'],
      ['gitea', 'simpleGitea', 'Source on Gitea'],
    ] as const
  ).map(([needle, icon, title]) => ({
    matches: (url: string) => {
      try {
        return new URL(url).hostname.toLowerCase().includes(needle);
      } catch {
        return false;
      }
    },
    icon,
    title,
  }));

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
};

@Component({
  selector: 'app-server-browse',
  imports: [
    ContentHeader,
    NgIcon,
    RouterLink,
    DatePipe,
    HlmBadgeImports,
    HlmButtonImports,
    ButtonLoading,
    HlmInputImports,
    HlmResizableImports,
    HlmSelectImports,
  ],
  providers: [
    provideIcons({
      simpleModrinth,
      simpleBitbucket,
      simpleCodeberg,
      simpleGit,
      simpleGitea,
      simpleGithub,
      simpleGitlab,
      simpleSourcehut,
      lucideBug,
      lucideDownload,
      lucidePackage,
      lucideSearch,
      lucideSettings,
      lucideTriangleAlert,
      lucideUsers,
      lucideX,
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

      <hlm-resizable-group direction="horizontal" class="min-h-0 flex-1">
      <hlm-resizable-panel
        [defaultSize]="70"
        [minSize]="35"
        class="min-h-0"
        [class.max-lg:hidden]="selected() !== null"
      >
        <!-- The scroll lives here and not on the panel: BrnResizablePanel writes overflow:hidden
             as an inline style, which no class can outrank, and the results would just be clipped. -->
        <div #scrollBox class="h-full overflow-auto px-4">
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
            <a hlmBtn variant="outline" size="sm" routerLink="/">
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
              <li
                class="hover:bg-accent/40 flex cursor-pointer items-start gap-3 rounded-md border p-3"
                [class.border-primary]="selected()?.projectId === h.projectId"
                [class.bg-accent/40]="selected()?.projectId === h.projectId"
                (click)="select(h)"
              >
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
                    <!-- The title is the link out to Modrinth here too; the rest of the card opens
                         the pane, so the click must not do both. -->
                    @if (projectUrl(h); as url) {
                      <a
                        class="truncate text-sm font-medium hover:underline"
                        [href]="url"
                        target="_blank"
                        rel="noopener noreferrer"
                        (click)="$event.stopPropagation()"
                        >{{ h.title }}</a
                      >
                    } @else {
                      <span class="truncate text-sm font-medium">{{ h.title }}</span>
                    }
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

                <!-- stopPropagation, because the card itself opens the pane and a click on Add
                     is not a click on the card. The Modrinth link is not here: it lives in the
                     pane, which is where someone reading about a mod already is. -->
                <div class="flex shrink-0 items-center gap-1" (click)="$event.stopPropagation()">
                  <button hlmBtn variant="outline" size="sm" type="button" (click)="versions(h)">
                    Versions
                  </button>
                  @if (h.installed) {
                    <span class="text-muted-foreground px-2 text-xs">Added</span>
                  } @else {
                    <button
                      [loading]="picking() === h.projectId"
                      hlmBtn
                      size="sm"
                      type="button"
                      [disabled]="picking() === h.projectId"
                      (click)="addLatest(h)"
                    >
                      {{ picking() === h.projectId ? 'Resolving' : 'Add' }}
                    </button>
                  }
                </div>
              </li>
            }
          </ul>

          <!-- One viewport tall and observed, so the next page is usually already there by the
               time the reader arrives. The button stays: a container that never scrolls never
               intersects, and then it is the only way on. -->
          <div #sentinel class="flex justify-center pb-6">
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
      </hlm-resizable-panel>

      <!-- Selection is a signal, not a route: on a route the back button would close the pane
           instead of leaving the page, and the grid would re-query on every click. Full width
           below lg, where two columns would squeeze the grid to one card per row. -->
      @if (selected(); as sel) {
        <hlm-resizable-handle withHandle class="max-lg:hidden" />
        <hlm-resizable-panel
          [defaultSize]="30"
          [minSize]="20"
          [maxSize]="60"
          class="bg-background min-h-0 w-full"
        >
          <div class="flex h-full flex-col overflow-auto">
          <div class="flex items-start gap-3 border-b p-4">
            @if (sel.iconUrl) {
              <img
                [src]="sel.iconUrl"
                [alt]="sel.title"
                class="size-12 shrink-0 rounded-md object-cover"
              />
            }
            <div class="flex min-w-0 flex-1 flex-col gap-0.5">
              <!-- The title is the way out to Modrinth, so the pane needs no button for it. -->
              @if (projectUrl(sel); as url) {
                <a
                  class="text-sm font-medium hover:underline"
                  [href]="url"
                  target="_blank"
                  rel="noopener noreferrer"
                  >{{ sel.title }}</a
                >
              } @else {
                <span class="text-sm font-medium">{{ sel.title }}</span>
              }
              @if (sel.author) {
                <span class="text-muted-foreground text-xs">by {{ sel.author }}</span>
              }
            </div>
            <button
              hlmBtn
              variant="ghost"
              size="icon-sm"
              type="button"
              title="Close"
              (click)="clearSelection()"
            >
              <ng-icon name="lucideX" size="14" />
            </button>
          </div>

          @if (detailLoading()) {
            <p class="text-muted-foreground p-4 text-xs">Loading details…</p>
          } @else if (detailFailed()) {
            <!-- A state, not a message: the failure itself was the toast, and a toast is gone in
                 seconds while an empty pane is not. -->
            <div class="flex flex-col items-center gap-2 p-8 text-center">
              <ng-icon name="lucideTriangleAlert" size="24" class="text-muted-foreground opacity-60" />
              <button hlmBtn variant="outline" size="sm" type="button" (click)="retryDetail()">
                Try again
              </button>
            </div>
          } @else if (detail(); as p) {
            <div class="flex flex-col gap-3 p-4">
              <div class="text-muted-foreground flex flex-wrap items-center gap-3 text-xs">
                <span class="inline-flex items-center gap-1">
                  <ng-icon name="lucideDownload" size="12" />
                  {{ compact(p.downloads) }}
                </span>
                <span class="inline-flex items-center gap-1">
                  <ng-icon name="lucideUsers" size="12" />
                  {{ compact(p.followers) }}
                </span>
                @if (sel.dateModified) {
                  <span>updated {{ sel.dateModified | date: 'mediumDate' }}</span>
                }
              </div>

              <div class="-mt-1 flex items-center gap-2">
                <div class="flex flex-1 flex-wrap items-center gap-1">
                  @for (c of p.categories; track c) {
                    <span hlmBadge variant="outline" class="text-xs">{{ c }}</span>
                  }
                </div>

                <!-- The forge the source is hosted on, named by its own mark. A host nobody
                     recognises still gets the generic git icon rather than no way through. -->
                <div class="flex shrink-0 items-center gap-1">
                  @if (p.sourceUrl) {
                    <a
                      hlmBtn
                      variant="ghost"
                      size="icon-xs"
                      [title]="sourceTitle(p.sourceUrl)"
                      [href]="p.sourceUrl"
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      <ng-icon [name]="sourceIcon(p.sourceUrl)" size="14" />
                    </a>
                  }
                  @if (p.issuesUrl) {
                    <a
                      hlmBtn
                      variant="ghost"
                      size="icon-xs"
                      title="Issue tracker"
                      [href]="p.issuesUrl"
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      <ng-icon name="lucideBug" size="14" />
                    </a>
                  }
                </div>
              </div>

              @if (body(); as html) {
                <!-- innerHTML on purpose: Angular's sanitiser runs on it, which is what makes a
                     third party's Markdown safe to show at all. -->
                <div class="hopper-markdown text-xs leading-relaxed" [innerHTML]="html"></div>
              } @else if (p.description) {
                <p class="text-muted-foreground text-xs">{{ p.description }}</p>
              }
            </div>
          }
          </div>
        </hlm-resizable-panel>
      }
      </hlm-resizable-group>
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

  protected readonly selected = signal<ModrinthSearchHitDto | null>(null);
  protected readonly detail = signal<ModrinthProjectDto | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailFailed = signal(false);
  private readonly detailRetry = signal(0);

  private readonly detailKey = computed(() => ({
    hit: this.selected(),
    retry: this.detailRetry(),
  }));

  // Modrinth allows 300 requests a minute and a browse page can burn that clicking back and forth,
  // so a project is fetched once and kept for as long as the page lives.
  private readonly detailCache = new Map<string, ModrinthProjectDto>();

  protected readonly body = computed(() => {
    const raw = this.detail()?.body;
    return raw != null && raw.trim() !== '' ? renderProjectBody(raw) : null;
  });

  private readonly sentinel = viewChild<ElementRef<HTMLElement>>('sentinel');
  private readonly scrollBox = viewChild<ElementRef<HTMLElement>>('scrollBox');
  private observer: IntersectionObserver | null = null;

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
    };
  });

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id !== '') this.load(id);
    });

    // afterNextRender rather than the constructor: there is no IntersectionObserver on the server,
    // and the sentinel does not exist until the results have rendered at least once.
    afterNextRender(() => this.observe());

    // Re-observes when the sentinel is replaced by a new search, and stops once there is nothing
    // left to fetch, so an idle page at the end of the results costs nothing.
    effect(() => {
      this.sentinel();
      if (this.hasMore()) this.observe();
      else this.disconnect();
    });

    this.destroyRef.onDestroy(() => this.disconnect());

    // switchMap so clicking a second card before the first answers drops the first: the pane must
    // never fill in with the project the reader has already moved on from.
    toObservable(this.detailKey)
      .pipe(
        switchMap(({ hit }) => {
          if (hit === null) {
            this.detail.set(null);
            return EMPTY;
          }

          const cached = this.detailCache.get(hit.projectId);
          if (cached) {
            this.detail.set(cached);
            return EMPTY;
          }

          this.detail.set(null);
          this.detailFailed.set(false);
          this.detailLoading.set(true);
          return this.api.apiModrinthProjectsIdOrSlugGet(hit.slug ?? hit.projectId).pipe(
            // EMPTY keeps the outer subscription alive so the next card still fetches; failed is
            // what stops the pane sitting empty once the toast has gone.
            catchError((err: unknown) => {
              toast.error(messageFrom(err, 'Failed to load this mod from Modrinth'));
              this.detailLoading.set(false);
              this.detailFailed.set(true);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((project) => {
        this.detailCache.set(project.id, project);
        this.detail.set(project);
        this.detailFailed.set(false);
        this.detailLoading.set(false);
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

  protected sourceIcon(url: string): string {
    return SOURCE_HOSTS.find((h) => h.matches(url))?.icon ?? 'simpleGit';
  }

  protected sourceTitle(url: string): string {
    return SOURCE_HOSTS.find((h) => h.matches(url))?.title ?? 'Source code';
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

  protected select(hit: ModrinthSearchHitDto): void {
    this.selected.set(this.selected()?.projectId === hit.projectId ? null : hit);
  }

  // A counter rather than re-setting the selection: a signal set to null and back in one tick
  // coalesces to no change at all, so the switchMap would never see a second value.
  protected retryDetail(): void {
    this.detailRetry.update((tick) => tick + 1);
  }

  protected clearSelection(): void {
    this.selected.set(null);
  }

  protected compact(value: unknown): string {
    return formatCount(toNumber(value));
  }

  protected loadMore(): void {
    if (this.loading() || !this.hasMore()) return;
    this.offset.set(this.hits().length);
  }

  private observe(): void {
    const target = this.sentinel()?.nativeElement;
    if (!target) return;

    // Reconnected rather than reused when the sentinel is recreated: it lives inside the @if that
    // the empty and error states replace, so the node identity does not survive a new search.
    this.observer?.disconnect();

    // root is the scrolling box, not the viewport. An IntersectionObserver intersects the root
    // clipped by every scrolling ancestor, so against the viewport a rootMargin would be cut back
    // to this box anyway and the fetch would only start once the end was nearly on screen.
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) this.loadMore();
      },
      { root: this.scrollBox()?.nativeElement ?? null, rootMargin: `${LOOKAHEAD_VIEWPORTS * 100}% 0px` },
    );

    observer.observe(target);
    this.observer = observer;
  }

  private disconnect(): void {
    this.observer?.disconnect();
    this.observer = null;
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
    await this.plan(pick.versionId, pick.title, hit.projectId);
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
          await this.plan(newest.id, hit.title, hit.projectId);
        },
        error: (err) => {
          this.picking.set(null);
          toast.error(messageFrom(err, 'Failed to load the versions of this mod'));
        },
      });
  }

  private async plan(versionId: string, title: string, projectId: string): Promise<void> {
    const result = await this.planDialog.add({
      serverId: this.serverId(),
      rootVersionIds: [versionId],
      rootTitles: [title],
    });
    if (!result) return;

    const added = result.installed.length + result.adopted.length + result.replaced.length;
    if (added > 0) {
      // Names the mod, because add() may have skipped the dialog entirely and four files arriving
      // after one click with no feedback is worse than the extra click it replaced.
      const extra = added - 1;
      toast.success(
        extra > 0
          ? `Added ${title} and ${extra} required dependenc${extra === 1 ? 'y' : 'ies'}`
          : `Added ${title} to this server`,
      );
    }

    // Marked here rather than re-queried. A refresh restarts at offset 0, which the subscription
    // treats as a replacement, so everything scrolled since would be thrown away and the reader
    // dropped back at the top. The one thing the refresh was for is this flag.
    this.markInstalled(projectId);
  }

  private markInstalled(projectId: string): void {
    this.hits.update((current) =>
      current.map((h) => (h.projectId === projectId ? { ...h, installed: true } : h)),
    );
    this.selected.update((sel) =>
      sel !== null && sel.projectId === projectId ? { ...sel, installed: true } : sel,
    );
  }

  private resetPaging(): void {
    this.offset.set(0);
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
