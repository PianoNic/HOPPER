import { Injectable, inject } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { toast } from '@spartan-ng/brain/sonner';

@Injectable({ providedIn: 'root' })
export class SessionRecovery {
  private readonly oidc = inject(OidcSecurityService);
  private redirecting = false;

  // The latch is the point. A dead session 401s every call of a forkJoin within one tick, and the
  // Overview page fires three, so without it one expiry is three redirects and three toasts. It is
  // never reset: the page is already on its way to the identity provider.
  recover(): void {
    if (this.redirecting) return;
    this.redirecting = true;

    toast.error('Your session expired. Sending you back to sign in.');
    this.oidc.authorize();
  }
}
