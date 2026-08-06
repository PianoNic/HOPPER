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
import { ServerDto } from '../api/model/serverDto';

/** One sidebar link. `exact` marks the ones that prefix-match their own children. */
type NavItem = { route: string; label: string; icon: string; exact: boolean };

// Everything under /server/<uuid>. Singular on purpose: /servers is the list, /server/<id> is one
// of them. The id is not validated as a UUID: the router already only
// produces this shape, and a stricter pattern here would silently drop the whole section if the
// route parameter ever changed form.
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

  // Spartan cookies the open/closed state itself, but we mirror it into localStorage so the
  // choice survives a cookie clear as well.
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
        /* private mode */
      }
    });
  }

  // routerLinkActive sets classes, not attributes, so it cannot drive the helm menu button's
  // [isActive] input. Derive the current URL once and let each item match against it instead.
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

  /**
   * The open server's name, for the group heading. Fetched per id rather than by filtering a cached
   * list, so the heading is right on a deep link into a server the sidebar has never listed. A
   * failed fetch falls back to no name rather than a toast: the section still navigates, and the
   * page the admin is looking at raises the real error already.
   */
  private readonly currentServer = toSignal(
    toObservable(this.currentServerId).pipe(
      switchMap((id) =>
        id === null
          ? of(null)
          : this.serversService.apiServersIdGet(id).pipe(catchError(() => of(null))),
      ),
    ),
    { initialValue: null },
  );

  protected readonly currentServerName = computed(() => this.currentServer()?.name ?? '');

  /**
   * Every server, for the picker. Refetched on navigation rather than once at startup, so a server
   * created on the list page is in the dropdown immediately instead of after a reload. The payload
   * is a handful of rows; a stale picker would be the more expensive mistake.
   */
  protected readonly servers = toSignal(
    toObservable(this.currentUrl).pipe(
      switchMap(() =>
        this.serversService.apiServersGet().pipe(catchError(() => of<ServerDto[]>([]))),
      ),
    ),
    { initialValue: [] as ServerDto[] },
  );

  /**
   * Switching keeps the sub-page: from one server's Mods you land on the next server's Mods, not
   * its overview. Picking from anywhere else opens the overview.
   */
  protected switchServer(id: string): void {
    const url = this.currentUrl();
    const match = SERVER_ROUTE.exec(url);
    const tail = match ? url.slice(match[0].length) : '';
    void this.router.navigateByUrl(`/server/${id}${tail}`);
  }

  protected isRouteActive(route: string, exact: boolean): boolean {
    const url = this.currentUrl();
    // '/' and a server's overview both prefix-match their own children, so they only light up on
    // an exact match; everything else is a leaf and can match its own subtree.
    if (exact) return url === route;
    return url === route || url.startsWith(route + '/');
  }

  protected readonly themeMode = this.theme.mode;

  protected readonly rootNav: ReadonlyArray<NavItem> = [
    { route: '/', label: 'Home', icon: 'lucideLayoutDashboard', exact: true },
    { route: '/servers', label: 'Servers', icon: 'lucideServer', exact: false },
  ];

  protected readonly serverNav = computed<ReadonlyArray<NavItem>>(() => {
    const id = this.currentServerId();
    if (id === null) return [];
    const base = `/server/${id}`;
    return [
      { route: base, label: 'Overview', icon: 'lucideLayoutDashboard', exact: true },
      { route: `${base}/mods`, label: 'Mods', icon: 'lucidePackage', exact: false },
      { route: `${base}/clients`, label: 'Clients', icon: 'lucideUsers', exact: false },
      // Listed unconditionally rather than only when something is open: the sidenav would have to
      // poll the pending endpoint per server to know, and a link that appears and disappears is a
      // worse way back to a checklist than one that is always in the same place.
      { route: `${base}/pending`, label: 'Fetch by hand', icon: 'lucideClipboardList', exact: false },
      { route: `${base}/setup`, label: 'Setup', icon: 'lucideBookOpen', exact: false },
    ];
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
