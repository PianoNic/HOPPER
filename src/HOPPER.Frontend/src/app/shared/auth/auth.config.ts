import { StsConfigHttpLoader, StsConfigLoader } from 'angular-auth-oidc-client';
import { NEVER, timer } from 'rxjs';
import { catchError, map, retry, tap } from 'rxjs/operators';
import { AppService } from '../../api/api/app.service';
import { environment } from '../environments/environment';
import {
  UnconfiguredOidcError,
  missingOidcSettings,
  showBootstrapFailure,
} from './bootstrap-failure';

const RETRY_DELAYS_MS = [500, 1500, 3000];

export const authLoaderFactory = (appService: AppService) => {
  const config$ = appService.apiAppGet().pipe(
    retry({
      count: RETRY_DELAYS_MS.length,
      delay: (_, attempt) => timer(RETRY_DELAYS_MS[attempt - 1]),
    }),

    tap((app) => {
      const missing = missingOidcSettings(app);
      if (missing.length > 0) throw new UnconfiguredOidcError(missing);
    }),
    map((app) => ({
      authority: app.authority ?? '',
      redirectUrl: app.redirectUri ?? '',
      postLogoutRedirectUri: app.postLogoutRedirectUri ?? '',
      clientId: app.clientId ?? '',
      scope: app.scope ?? '',
      responseType: 'code',
      silentRenew: true,
      useRefreshToken: true,
      renewTimeBeforeTokenExpiresInSeconds: 30,

      secureRoutes: [environment.apiBaseUrl],
    })),

    catchError((err: unknown) => {
      console.error(err);
      if (err instanceof UnconfiguredOidcError) {
        showBootstrapFailure('unconfigured', err.missing);
      } else {
        showBootstrapFailure('unreachable');
      }
      return NEVER;
    }),
  );
  return new StsConfigHttpLoader(config$);
};

export const authLoaderProvider = {
  provide: StsConfigLoader,
  useFactory: authLoaderFactory,
  deps: [AppService],
};
