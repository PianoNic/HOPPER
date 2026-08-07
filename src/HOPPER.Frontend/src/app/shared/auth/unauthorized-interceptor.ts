import { HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, tap, throwError } from 'rxjs';
import { environment } from '../environments/environment';
import { SessionRecovery } from './session-recovery';

export const unauthorizedInterceptor: HttpInterceptorFn = (req, next) => {
  const recovery = inject(SessionRecovery);

  return next(req).pipe(
    tap((event) => {
      if (event instanceof HttpResponse && isApiRequest(req.url)) recovery.clear();
    }),
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && isApiRequest(req.url)) {
        recovery.recover();
      }

      return throwError(() => err);
    }),
  );
};

export function isApiRequest(url: string): boolean {
  return url.startsWith(`${environment.apiBaseUrl.replace(/\/+$/, '')}/api/`);
}
