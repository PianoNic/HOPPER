import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { catchError, filter, map, of, startWith, switchMap } from 'rxjs';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBookOpen,
  lucideChevronsUpDown,
  lucideClipboardList,
  lucideLayoutDashboard,
  lucideLogOut,
  lucideMonitor,
  lucideMoon,
  lucidePackage,
  lucideSearch,
  lucideServer,
  lucideSun,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { ThemeService, ThemeMode } from '../shared/services/theme.service';
import { AppService } from '../api/api/app.service';
import { ServersService } from '../api/api/servers.service';
import { ServerChanged } from '../shared/services/server-changed';
import { toNumber } from '../shared/utils/format';
import { ServerDto } from '../api/model/serverDto';

type NavItem = { route: string | null; label: string; icon: string; exact: boolean; count?: number };

const SERVER_ROUTE = /^\/server\/([^/]+)/;

@Component({
  selector: 'app-sidenav',
  imports: [HlmSidebarImports, HlmDropdownMenuImports, HlmAvatarImports, NgIcon, RouterLink],
  providers: [
    provideIcons({
      lucideBookOpen,
      lucideChevronsUpDown,
      lucideClipboardList,
      lucideLayoutDashboard,
      lucideLogOut,
      lucideMonitor,
      lucideMoon,
      lucidePackage,
      lucideSearch,
      lucideServer,
      lucideSun,
      lucideUsers,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sidenav.html',
})
export class Sidenav {
  private readonly sidebarService = inject(HlmSidebarService);
  private readonly oidcSecurityService = inject(OidcSecurityService);
  private readonly appService = inject(AppService);
  private readonly serversService = inject(ServersService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly theme = inject(ThemeService);
  private readonly router = inject(Router);

  private static readonly STORAGE_KEY = 'hopper.sidebar.open';

  constructor() {
    const saved =
      typeof localStorage !== 'undefined' ? localStorage.getItem(Sidenav.STORAGE_KEY) : null;
    if (saved !== null) {
      this.sidebarService.setOpen(saved === 'true');
    }
    effect(() => {
      const open = this.sidebarService.open();
      try {
        localStorage.setItem(Sidenav.STORAGE_KEY, String(open));
      } catch {
      }
    });
  }

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly currentServerId = computed(() => {
    const match = SERVER_ROUTE.exec(this.currentUrl());
    return match ? match[1] : null;
  });

  private readonly serverChanged = inject(ServerChanged);

  private readonly currentServer = toSignal(
    toObservable(computed(() => ({ id: this.currentServerId(), rev: this.serverChanged.revision() }))).pipe(
      switchMap(({ id }) =>
        id === null
          ? of(null)
          : this.serversService.apiServersIdGet(id).pipe(catchError(() => of(null))),
      ),
    ),
    { initialValue: null },
  );

  protected readonly counts = computed<Record<string, number | undefined>>(() => {
    const server = this.currentServer();
    return {
      '/mods': server === undefined || server === null ? undefined : toNumber(server.modCount),
      '/clients': server === undefined || server === null ? undefined : toNumber(server.clientCount),
    };
  });

  protected readonly currentServerName = computed(() => this.currentServer()?.name ?? '');

  protected readonly servers = toSignal(
    toObservable(this.currentUrl).pipe(
      switchMap(() =>
        this.serversService.apiServersGet().pipe(catchError(() => of<ServerDto[]>([]))),
      ),
    ),
    { initialValue: [] as ServerDto[] },
  );

  protected switchServer(id: string): void {
    const url = this.currentUrl();
    const match = SERVER_ROUTE.exec(url);
    const tail = match ? url.slice(match[0].length) : '';
    void this.router.navigateByUrl(`/server/${id}${tail}`);
  }

  protected isRouteActive(route: string | null, exact: boolean): boolean {
    if (route === null) return false;
    const url = this.currentUrl();

    if (exact) return url === route;
    return url === route || url.startsWith(route + '/');
  }

  protected readonly themeMode = this.theme.mode;

  protected readonly rootNav: ReadonlyArray<NavItem> = [
    { route: '/', label: 'Servers', icon: 'lucideServer', exact: true },
  ];

  private static readonly SERVER_PAGES: ReadonlyArray<Omit<NavItem, 'route'> & { suffix: string }> =
    [
      { suffix: '', label: 'Overview', icon: 'lucideLayoutDashboard', exact: true },
      { suffix: '/mods', label: 'Mods', icon: 'lucidePackage', exact: false },
      { suffix: '/browse', label: 'Browse mods', icon: 'lucideSearch', exact: false },
      { suffix: '/clients', label: 'Clients', icon: 'lucideUsers', exact: false },
      { suffix: '/pending', label: 'Fetch by hand', icon: 'lucideClipboardList', exact: false },
      { suffix: '/setup', label: 'Setup', icon: 'lucideBookOpen', exact: false },
    ];

  protected readonly serverNav = computed<ReadonlyArray<NavItem>>(() => {
    const id = this.currentServerId();
    return Sidenav.SERVER_PAGES.map(({ suffix, ...rest }) => ({
      ...rest,
      route: id === null ? null : `/server/${id}${suffix}`,
      count: this.counts()[suffix],
    }));
  });

  protected readonly themeOptions: ReadonlyArray<{ mode: ThemeMode; label: string; icon: string }> =
    [
      { mode: 'light', label: 'Light', icon: 'lucideSun' },
      { mode: 'dark', label: 'Dark', icon: 'lucideMoon' },
      { mode: 'system', label: 'System', icon: 'lucideMonitor' },
    ];

  protected readonly menuSide = computed(() => (this.sidebarService.isMobile() ? 'top' : 'right'));

  protected readonly version = toSignal(
    this.appService.apiAppGet().pipe(map((app) => app.version ?? '')),
    { initialValue: '' },
  );

  private readonly userData = this.oidcSecurityService.userData;
  protected readonly user = computed(() => {
    const data = this.userData().userData;
    return {
      name: data?.preferred_username ?? data?.email ?? '',
      email: data?.email ?? '',
      avatar: data?.picture ?? '',
    };
  });

  protected setTheme(mode: ThemeMode): void {
    this.theme.set(mode);
  }

  protected logout(): void {
    this.oidcSecurityService
      .logoffAndRevokeTokens()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }
}
