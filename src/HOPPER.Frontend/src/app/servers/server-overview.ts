import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { toast } from '@spartan-ng/brain/sonner';
import {
  lucideArrowUpFromLine,
  lucideCircleCheck,
  lucideCircleDashed,
  lucideDownload,
  lucideHardDrive,
  lucideRefreshCw,
  lucideTriangleAlert,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ButtonLoading } from '../shared/directives/button-loading';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ContentHeader } from '../shared/components/content-header/content-header';
import {
  ChartCanvas,
  ChartFactory,
  CHART_CATEGORY,
  CHART_STATUS,
  countInSegment,
  tooltipStyle,
  valueAtTip,
} from '../shared/components/chart/chart-canvas';
import { formatBytes, messageFrom, toNumber } from '../shared/utils/format';
import { downloadServerJar } from '../shared/utils/download';
import {
  ClientDrift,
  countDrift,
  diffClient,
  downloadSizes,
  OFFLINE_AFTER_LABEL,
} from '../shared/utils/drift';
import { ServersService } from '../api/api/servers.service';
import { ServerClientsService } from '../api/api/serverClients.service';
import { ServerModsService } from '../api/api/serverMods.service';
import { ClientDto } from '../api/model/clientDto';
import { ModDto } from '../api/model/modDto';
import { ServerDto } from '../api/model/serverDto';
import { serverIdSignal } from './server-route';

const LARGEST_MODS = 8;
const ROW_HEIGHT = 30;

const DAY = new Intl.DateTimeFormat(undefined, { day: '2-digit', month: 'short' });

function shortName(fileName: string): string {
  const base = fileName.replace(/\.jar$/i, '');
  return base.length > 24 ? `${base.slice(0, 23)}…` : base;
}

type Slice = { label: string; color: string; count: number };

function tally<T extends string>(
  mods: readonly ModDto[],
  of: (mod: ModDto) => T,
  named: ReadonlyArray<readonly [T, string]>,
): Slice[] {
  return named
    .map(([value, label], i) => ({
      label,
      color: CHART_CATEGORY[i % CHART_CATEGORY.length],
      count: mods.filter((mod) => of(mod) === value).length,
    }))
    .filter((slice) => slice.count > 0);
}

function doughnut(slices: readonly Slice[]): ChartFactory | null {
  if (slices.length === 0) return null;

  return (t) => ({
    type: 'doughnut',
    data: {
      labels: slices.map((slice) => slice.label),
      datasets: [
        {
          data: slices.map((slice) => slice.count),
          backgroundColor: slices.map((slice) => slice.color),
          // Painted in the card's own colour so touching arcs read as separate at any size.
          borderColor: t.surface,
          borderWidth: 2,
        },
      ],
    },
    options: {
      animation: false,
      maintainAspectRatio: false,
      cutout: '58%',
      plugins: {
        tooltip: {
          ...tooltipStyle(t),
          callbacks: { title: () => '', label: (item) => `${item.label} - ${item.parsed}` },
        },
      },
    },
  });
}

@Component({
  selector: 'app-server-overview',
  imports: [
    ContentHeader,
    RouterLink,
    NgIcon,
    HlmButtonImports,
    ButtonLoading,
    HlmCardImports,
    ChartCanvas,
  ],
  providers: [
    provideIcons({
      lucideArrowUpFromLine,
      lucideCircleCheck,
      lucideCircleDashed,
      lucideDownload,
      lucideHardDrive,
      lucideRefreshCw,
      lucideTriangleAlert,
      lucideUsers,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header>
      <span slot="left" class="truncate text-sm font-medium">{{ serverName() }}</span>
    </app-content-header>

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">Overview</h2>
        <div class="flex items-center gap-2">
          <button
            [loading]="loading()"
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            (click)="reload()"
            [disabled]="loading()"
          >
            <ng-icon name="lucideRefreshCw" size="14" />
            {{ loading() ? 'Loading' : 'Refresh' }}
          </button>
          <button
            hlmBtn
            size="sm"
            type="button"
            title="The same file goes in a player's mods folder and a dedicated server's - it works out which side it is on by itself"
            [disabled]="building()"
            (click)="downloadJar()"
            [loading]="building()"
          >
            <ng-icon name="lucideDownload" size="14" />
            {{ building() ? 'Building' : 'Download jar' }}
          </button>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        @if (attention(); as problems) {
          @if (problems.length > 0) {
            <a
              [routerLink]="modsLink()"
              class="border-destructive/40 bg-destructive/5 hover:bg-destructive/10 mb-3 flex items-center gap-2 rounded-lg border px-3 py-2 text-sm"
            >
              <ng-icon name="lucideTriangleAlert" size="16" class="text-destructive shrink-0" />
              <span>
                @for (problem of problems; track problem.label) {
                  <span class="font-medium tabular-nums">{{ problem.count }}</span>
                  <span class="text-muted-foreground">
                    {{ problem.count === 1 ? 'mod' : 'mods' }} {{ problem.label
                    }}{{ $last ? '' : ' · ' }}
                  </span>
                }
              </span>
              <span class="text-muted-foreground ml-auto text-xs">Open Mods</span>
            </a>
          }
        }

        <div class="grid gap-3 sm:grid-cols-3">
          @for (stat of stats(); track stat.label) {
            <section hlmCard>
              <div hlmCardHeader class="flex flex-row items-center justify-between gap-2 pb-2">
                <h3 hlmCardDescription class="text-xs">{{ stat.label }}</h3>
                <ng-icon [name]="stat.icon" size="16" class="text-muted-foreground" />
              </div>
              <div hlmCardContent>
                <p class="text-2xl font-semibold tabular-nums">{{ stat.value }}</p>
                <p class="text-muted-foreground mt-1 text-xs">{{ stat.hint }}</p>
              </div>
            </section>
          }
        </div>

        <div class="mt-3 grid items-start gap-3 lg:grid-cols-3">
          <div class="flex flex-col gap-3 lg:col-span-2">
            <section hlmCard>
              <div hlmCardHeader class="pb-2">
                <h3 hlmCardTitle class="text-sm">Largest mods</h3>
                <p hlmCardDescription class="text-xs">
                  The biggest jars in the manifest - what the first sync spends its time on
                </p>
              </div>
              <div hlmCardContent>
                @if (largestMods(); as build) {
                  <div [style.height.px]="largestModsHeight()">
                    <app-chart-canvas [build]="build" />
                  </div>
                } @else {
                  <p class="text-muted-foreground text-sm">No mods on this server yet.</p>
                }
              </div>
            </section>

            <section hlmCard>
              <div hlmCardHeader class="pb-2">
                <h3 hlmCardTitle class="text-sm">How this library grew</h3>
                <p hlmCardDescription class="text-xs">
                  Everything a player downloads, as it accumulated - a step is a batch added at once
                </p>
              </div>
              <div hlmCardContent>
                @if (growth(); as build) {
                  <div class="h-[200px]">
                    <app-chart-canvas [build]="build" />
                  </div>
                } @else {
                  <p class="text-muted-foreground text-sm">
                    {{
                      mods().length === 0
                        ? 'No mods on this server yet.'
                        : 'Everything here was added the day the server was, so there is no curve to draw yet.'
                    }}
                  </p>
                }
              </div>
            </section>
          </div>

          <div class="flex flex-col gap-3">
            <section hlmCard>
              <div hlmCardHeader class="pb-2">
                <h3 hlmCardTitle class="text-sm">What each side receives</h3>
                <p hlmCardDescription class="text-xs">
                  Sides decide who gets which jar, so the two totals differ
                </p>
              </div>
              <div hlmCardContent>
                @if (sideSizes(); as build) {
                  <div class="h-[72px]">
                    <app-chart-canvas [build]="build" />
                  </div>
                } @else {
                  <p class="text-muted-foreground text-sm">No mods on this server yet.</p>
                }
              </div>
            </section>

            <section hlmCard>
              <div hlmCardHeader class="pb-2">
                <h3 hlmCardTitle class="text-sm">Client state</h3>
                <p hlmCardDescription class="text-xs">
                  {{ clients().length }} known client{{ clients().length === 1 ? '' : 's' }}
                </p>
              </div>
              <div hlmCardContent>
                @if (clientStateChart(); as build) {
                  <div class="h-[34px]">
                    <app-chart-canvas [build]="build" />
                  </div>
                  <ul class="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs">
                    @for (part of clientState(); track part.label) {
                      <li class="flex items-center gap-1.5">
                        <ng-icon [name]="part.icon" size="13" [style.color]="part.color" />
                        <span class="text-muted-foreground">{{ part.label }}</span>
                        <span class="font-medium tabular-nums">{{ part.count }}</span>
                      </li>
                    }
                  </ul>
                } @else {
                  <p class="text-muted-foreground text-sm">
                    No client has launched with this server's jar yet.
                  </p>
                }
              </div>
            </section>

            @for (split of splits(); track split.title) {
              <section hlmCard>
                <div hlmCardHeader class="pb-2">
                  <h3 hlmCardTitle class="text-sm">{{ split.title }}</h3>
                  <p hlmCardDescription class="text-xs">{{ split.hint }}</p>
                </div>
                <div hlmCardContent>
                  @if (split.build) {
                    <div class="flex items-center gap-4">
                      <div class="h-[104px] w-[104px] shrink-0">
                        <app-chart-canvas [build]="split.build" />
                      </div>
                      <ul class="min-w-0 flex-1 space-y-1 text-xs">
                        @for (slice of split.slices; track slice.label) {
                          <li class="flex items-center gap-1.5">
                            <span
                              class="size-2.5 shrink-0 rounded-[2px]"
                              [style.background-color]="slice.color"
                            ></span>
                            <span class="text-muted-foreground truncate">{{ slice.label }}</span>
                            <span class="ml-auto font-medium tabular-nums">{{ slice.count }}</span>
                          </li>
                        }
                      </ul>
                    </div>
                  } @else {
                    <p class="text-muted-foreground text-sm">No mods on this server yet.</p>
                  }
                </div>
              </section>
            }
          </div>
        </div>
      </div>
    </section>
  `,
})
export class ServerOverview {
  private readonly route = inject(ActivatedRoute);
  private readonly serversApi = inject(ServersService);
  private readonly modsApi = inject(ServerModsService);
  private readonly clientsApi = inject(ServerClientsService);

  protected readonly serverId = serverIdSignal(this.route);

  protected readonly modsLink = computed(() => ['/server', this.serverId(), 'mods']);

  protected readonly server = signal<ServerDto | null>(null);
  protected readonly mods = signal<ReadonlyArray<ModDto>>([]);
  protected readonly clients = signal<ReadonlyArray<ClientDto>>([]);
  protected readonly loading = signal(false);
  protected readonly building = signal(false);

  private readonly now = signal(Date.now());

  protected readonly serverName = computed(() => this.server()?.name ?? '');

  protected readonly drift = computed<ReadonlyArray<ClientDrift>>(() => {
    const mods = this.mods();
    const now = this.now();
    return this.clients().map((client) => diffClient(client, mods, now));
  });

  /// Every condition the Mods page badges, counted here so a broken library is visible without
  /// opening that page and reading every row.
  protected readonly attention = computed(() => {
    const mods = this.mods();

    const counts = [
      { label: 'with no bytes behind them', count: mods.filter((m) => m.bytesMissing).length },
      { label: 'sharing a mod id', count: mods.filter((m) => m.collidesOn).length },
      {
        label: 'missing a dependency',
        count: mods.filter((m) => m.missingDependencies?.length).length,
      },
    ];

    return counts.filter((c) => c.count > 0);
  });

  protected readonly stats = computed(() => {
    const sizes = downloadSizes(this.mods());
    const sameBothWays = sizes.client === sizes.server;
    const { active } = countDrift(this.drift());

    return [
      {
        label: 'Download size',
        value: sameBothWays
          ? formatBytes(sizes.client)
          : `${formatBytes(sizes.client)} / ${formatBytes(sizes.server)}`,
        hint: sameBothWays
          ? 'Fetched once, then cached by hash'
          : `To a player / to a dedicated server, of ${formatBytes(sizes.stored)} stored`,
        icon: 'lucideHardDrive',
      },
      {
        label: 'Served all time',
        value: formatBytes(toNumber(this.server()?.bytesServed)),
        hint: 'Jars and blobs this server has actually sent, since it was created',
        icon: 'lucideArrowUpFromLine',
      },
      {
        label: `Launched (${OFFLINE_AFTER_LABEL})`,
        value: `${active}`,
        hint: `${this.clients().length} known in total. HOPPER hears from a client when it launches.`,
        icon: 'lucideUsers',
      },
    ];
  });

  private readonly topMods = computed(() =>
    [...this.mods()]
      .sort((a, b) => toNumber(b.size) - toNumber(a.size))
      .slice(0, LARGEST_MODS),
  );

  protected readonly largestModsHeight = computed(() => this.topMods().length * ROW_HEIGHT + 12);

  protected readonly largestMods = computed<ChartFactory | null>(() => {
    const top = this.topMods();
    if (top.length === 0) return null;

    const names = top.map((mod) => mod.fileName);
    const values = top.map((mod) => toNumber(mod.size));
    const headroom = Math.max(...values, 1) * 1.22;

    return (t) => ({
      type: 'bar',
      data: {
        labels: names.map(shortName),
        datasets: [
          {
            data: values,
            backgroundColor: t.series,
            maxBarThickness: 24,
            borderRadius: { topLeft: 0, bottomLeft: 0, topRight: 4, bottomRight: 4 },
            borderSkipped: false,
          },
        ],
      },
      options: {
        indexAxis: 'y',
        animation: false,
        maintainAspectRatio: false,
        scales: {
          x: { display: false, beginAtZero: true, max: headroom },
          y: {
            grid: { display: false },
            border: { display: false },
            ticks: { color: t.ink, autoSkip: false },
          },
        },
        plugins: {
          tooltip: {
            ...tooltipStyle(t),
            callbacks: {
              title: () => '',
              label: (item) => `${names[item.dataIndex]} - ${formatBytes(values[item.dataIndex])}`,
            },
          },
        },
      },
      plugins: [valueAtTip(t, formatBytes)],
    });
  });

  protected readonly sideSizes = computed<ChartFactory | null>(() => {
    const mods = this.mods();
    if (mods.length === 0) return null;

    const sizes = downloadSizes(mods);
    const values = [sizes.client, sizes.server];
    const headroom = Math.max(...values, 1) * 1.22;

    return (t) => ({
      type: 'bar',
      data: {
        labels: ['A player', 'A dedicated server'],
        datasets: [
          {
            data: values,
            backgroundColor: t.series,
            maxBarThickness: 24,
            borderRadius: { topLeft: 0, bottomLeft: 0, topRight: 4, bottomRight: 4 },
            borderSkipped: false,
          },
        ],
      },
      options: {
        indexAxis: 'y',
        animation: false,
        maintainAspectRatio: false,
        scales: {
          x: { display: false, beginAtZero: true, max: headroom },
          y: {
            grid: { display: false },
            border: { display: false },
            ticks: { color: t.ink },
          },
        },
        plugins: {
          tooltip: {
            ...tooltipStyle(t),
            callbacks: { title: () => '', label: (item) => formatBytes(values[item.dataIndex]) },
          },
        },
      },
      plugins: [valueAtTip(t, formatBytes)],
    });
  });

  protected readonly growth = computed<ChartFactory | null>(() => {
    const mods = this.mods();
    if (mods.length === 0) return null;

    const byDay = new Map<string, number>();

    // The server's own day, carrying nothing. Without it a library filled in one sitting is a
    // single point in an empty plot, which reads as a broken chart rather than a young server.
    const born = this.server()?.createdAt?.slice(0, 10);
    if (born) byDay.set(born, 0);

    for (const mod of mods) {
      const day = mod.createdAt.slice(0, 10);
      byDay.set(day, (byDay.get(day) ?? 0) + toNumber(mod.size));
    }

    const days = [...byDay.keys()].sort();
    if (days.length < 2) return null;

    let running = 0;
    const totals = days.map((day) => (running += byDay.get(day)!));
    const labels = days.map((day) => DAY.format(new Date(`${day}T00:00:00`)));

    return (t) => ({
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            data: totals,
            borderColor: t.series,
            backgroundColor: `${t.series}22`,
            borderWidth: 2,
            // Nothing grew between two additions, so a slope between them would invent a history
            // the server does not have. 'before' is the arm that puts the rise on the day the mods
            // actually landed rather than on the one before it.
            stepped: 'before',
            fill: true,
            pointRadius: totals.length === 1 ? 4 : 2,
            pointBackgroundColor: t.series,
          },
        ],
      },
      options: {
        animation: false,
        maintainAspectRatio: false,
        scales: {
          x: {
            grid: { display: false },
            border: { display: false },
            ticks: { color: t.ink, maxRotation: 0, autoSkipPadding: 16 },
          },
          y: {
            beginAtZero: true,
            border: { display: false },
            grid: { color: `${t.ink}22` },
            ticks: { color: t.ink, maxTicksLimit: 5, callback: (value) => formatBytes(+value) },
          },
        },
        plugins: {
          tooltip: {
            ...tooltipStyle(t),
            callbacks: {
              title: () => '',
              label: (item) =>
                `${labels[item.dataIndex]} - ${formatBytes(totals[item.dataIndex])} in total`,
            },
          },
        },
      },
    });
  });

  private readonly bySide = computed(() =>
    tally(this.mods(), (mod) => mod.side, [
      ['Both', 'Both'],
      ['ClientOnly', 'Client only'],
      ['ServerOnly', 'Server only'],
    ]),
  );

  private readonly bySource = computed(() =>
    tally(this.mods(), (mod) => mod.source, [
      ['Modrinth', 'Modrinth'],
      ['Manual', 'Uploaded by hand'],
      ['Import', 'Pack import'],
    ]),
  );

  protected readonly splits = computed(() => [
    {
      title: 'Mods by side',
      hint: 'Who each jar is sent to',
      slices: this.bySide(),
      build: doughnut(this.bySide()),
    },
    {
      title: 'Where they came from',
      hint: 'How each jar reached this server',
      slices: this.bySource(),
      build: doughnut(this.bySource()),
    },
  ]);

  /* The neutral sits between the two chromatic states on purpose: green touching orange is the
     pair protanopes lose, and the gray between them keeps every adjacent pair separable. */
  protected readonly clientState = computed(() => {
    const rows = this.drift();
    const tally = (status: ClientDrift['status']): number =>
      rows.filter((row) => row.status === status).length;

    return [
      {
        label: 'In sync',
        icon: 'lucideCircleCheck',
        color: CHART_STATUS.good,
        count: tally('in sync'),
      },
      {
        label: `Not launched (${OFFLINE_AFTER_LABEL})`,
        icon: 'lucideCircleDashed',
        color: CHART_STATUS.neutral,
        count: tally('offline'),
      },
      {
        label: 'Behind',
        icon: 'lucideCircleDashed',
        color: CHART_CATEGORY[0],
        count: tally('behind'),
      },
      {
        label: 'Drifting',
        icon: 'lucideTriangleAlert',
        color: CHART_STATUS.serious,
        count: tally('drift'),
      },
    ];
  });

  protected readonly clientStateChart = computed<ChartFactory | null>(() => {
    const parts = this.clientState().filter((part) => part.count > 0);
    const total = parts.reduce((sum, part) => sum + part.count, 0);
    if (total === 0) return null;

    const last = parts.length - 1;

    return (t) => ({
      type: 'bar',
      data: {
        labels: [''],
        datasets: parts.map((part, i) => ({
          label: part.label,
          data: [part.count],
          backgroundColor: part.color,
          borderColor: t.surface,
          borderWidth: { top: 0, right: i === last ? 0 : 2, bottom: 0, left: 0 },
          borderSkipped: false,
          borderRadius: {
            topLeft: i === 0 ? 4 : 0,
            bottomLeft: i === 0 ? 4 : 0,
            topRight: i === last ? 4 : 0,
            bottomRight: i === last ? 4 : 0,
          },
          barThickness: 24,
        })),
      },
      options: {
        indexAxis: 'y',
        animation: false,
        maintainAspectRatio: false,
        scales: {
          x: { stacked: true, display: false, beginAtZero: true, max: total },
          y: { stacked: true, display: false },
        },
        plugins: {
          tooltip: {
            ...tooltipStyle(t),
            callbacks: {
              title: () => '',
              label: (item) => `${parts[item.datasetIndex].label} - ${item.parsed.x}`,
            },
          },
        },
      },
      plugins: [countInSegment()],
    });
  });

  constructor() {
    effect(() => {
      const id = this.serverId();
      if (id !== '') this.load(id);
    });
  }

  protected reload(): void {
    const id = this.serverId();
    if (id !== '') this.load(id);
  }

  private load(id: string): void {
    this.loading.set(true);

    forkJoin({
      server: this.serversApi.apiServersIdGet(id),
      mods: this.modsApi.apiServersIdModsGet(id),
      clients: this.clientsApi.apiServersIdClientsGet(id),
    }).subscribe({
      next: (result) => {
        this.server.set(result.server);
        this.mods.set(result.mods);
        this.clients.set(result.clients);
        this.now.set(Date.now());
        this.loading.set(false);
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to load the overview'));
        this.loading.set(false);
      },
    });
  }

  protected downloadJar(): void {
    const server = this.server();
    if (!server) return;

    downloadServerJar(this.serversApi, server, (running) => this.building.set(running));
  }
}
