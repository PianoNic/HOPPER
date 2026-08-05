import { StsConfigHttpLoader, StsConfigLoader } from 'angular-auth-oidc-client';
import { map } from 'rxjs/operators';
import { AppService } from '../../api/api/app.service';
import { environment } from '../environments/environment';

// OIDC settings come from GET /api/app rather than environment.ts: the same built bundle has to
// work against whatever IdP the server is pointed at, and only the server knows that.
export const authLoaderFactory = (appService: AppService) => {
  const config$ = appService.apiAppGet().pipe(
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
      // Only requests to the API get the bearer token attached. The manifest, blob and report
      // endpoints on that same origin take a shared client token instead, but the dashboard never
      // calls them, so there is no scheme collision to worry about here.
      secureRoutes: [environment.apiBaseUrl],
    })),
  );
  return new StsConfigHttpLoader(config$);
};

export const authLoaderProvider = {
  provide: StsConfigLoader,
  useFactory: authLoaderFactory,
  deps: [AppService],
};
