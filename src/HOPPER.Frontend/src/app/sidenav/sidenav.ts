import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBookOpen,
  lucideChevronsUpDown,
  lucideLayoutDashboard,
  lucideLogOut,
  lucideMonitor,
  lucideMoon,
  lucidePackage,
  lucideSun,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { ThemeService, ThemeMode } from '../shared/services/theme.service';
import { AppService } from '../api/api/app.service';

@Component({
  selector: 'app-sidenav',
  imports: [HlmSidebarImports, HlmDropdownMenuImports, HlmAvatarImports, NgIcon, RouterLink],
  providers: [
    provideIcons({
      lucideBookOpen,
      lucideChevronsUpDown,
      lucideLayoutDashboard,
      lucideLogOut,
      lucideMonitor,
      lucideMoon,
      lucidePackage,
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

  protected isRouteActive(route: string): boolean {
    const url = this.currentUrl();
    // '/' would prefix-match every route, so the overview only lights up on an exact match.
    if (route === '/') return url === '/';
    return url === route || url.startsWith(route + '/');
  }

  protected readonly themeMode = this.theme.mode;

  protected readonly navItems: ReadonlyArray<{ route: string; label: string; icon: string }> = [
    { route: '/', label: 'Overview', icon: 'lucideLayoutDashboard' },
    { route: '/mods', label: 'Mods', icon: 'lucidePackage' },
    { route: '/clients', label: 'Clients', icon: 'lucideUsers' },
    { route: '/setup', label: 'Setup', icon: 'lucideBookOpen' },
  ];

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
