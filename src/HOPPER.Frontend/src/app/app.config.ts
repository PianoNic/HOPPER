import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import {
  EventTypes,
  PublicEventsService,
  authInterceptor,
  provideAuth,
  withAppInitializerAuthCheck,
} from 'angular-auth-oidc-client';
import { filter } from 'rxjs/operators';

import { routes } from './app.routes';
import { provideApi } from './api/provide-api';
import { authLoaderProvider } from './shared/auth/auth.config';
import { SessionRecovery } from './shared/auth/session-recovery';
import { unauthorizedInterceptor } from './shared/auth/unauthorized-interceptor';
import { environment } from './shared/environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),

    provideHttpClient(withInterceptors([unauthorizedInterceptor, authInterceptor()])),
    provideApi(environment.apiBaseUrl),
    provideAuth({ loader: authLoaderProvider }, withAppInitializerAuthCheck()),

    provideAppInitializer(() => {
      const events = inject(PublicEventsService);
      const recovery = inject(SessionRecovery);
      events
        .registerForEvents()
        .pipe(filter((event) => event.type === EventTypes.SilentRenewFailed))
        .subscribe(() => recovery.recover());
    }),
  ],
};
