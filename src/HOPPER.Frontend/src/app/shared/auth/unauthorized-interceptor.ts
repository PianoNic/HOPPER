import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { environment } from '../environments/environment';
import { SessionRecovery } from './session-recovery';

// autoLoginPartialRoutesGuard only fires on navigation, so a session that dies while the user sits
// on one page never reaches it. This covers the in-place XHR case and nothing else.
export const unauthorizedInterceptor: HttpInterceptorFn = (req, next) => {
  // Hoisted deliberately: inject() inside catchError runs outside the injection context and throws.
  const recovery = inject(SessionRecovery);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && isApiRequest(req.url)) {
        recovery.recover();
      }
      // Rethrown so the page's own toast still fires once for the request that was in flight.
      return throwError(() => err);
    }),
  );
};

// A 401 from the identity provider's own token endpoint is the library's to handle; treating it as
// a dead session would start a redirect fight with it.
export function isApiRequest(url: string): boolean {
  return url.startsWith(environment.apiBaseUrl);
}
