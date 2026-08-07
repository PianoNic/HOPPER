import { Injectable, inject } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { toast } from '@spartan-ng/brain/sonner';

const STAMP = 'hopper.session-recovery';

@Injectable({ providedIn: 'root' })
export class SessionRecovery {
  private readonly oidc = inject(OidcSecurityService);
  private redirecting = false;

  // Two latches for two different loops. The field stops a forkJoin of three 401s in one tick from
  // firing three redirects. The stamp stops the redirect itself from repeating: authorize() takes
  // the whole document away, so the field is gone by the time the API 401s again, and an API that
  // rejects a freshly minted token loops forever with nothing on screen - the toast never paints
  // because the navigation kills the document first.
  recover(): void {
    if (this.redirecting) return;
    this.redirecting = true;

    if (this.alreadyTried()) {
      toast.error('Signing in did not get HOPPER a token it accepts. Check the identity provider.');
      return;
    }

    this.stamp();
    toast.error('Your session expired. Sending you back to sign in.');
    this.oidc.authorize();
  }

  private alreadyTried(): boolean {
    try {
      return sessionStorage.getItem(STAMP) !== null;
    } catch {
      // Storage denied, so the second attempt cannot be told from the first. Redirect once anyway:
      // an expired session that has to be signed in again beats never recovering at all.
      return false;
    }
  }

  private stamp(): void {
    try {
      sessionStorage.setItem(STAMP, '1');
    } catch {
      // As above.
    }
  }

  // Called once the API answers, which is the only proof the token is accepted.
  clear(): void {
    try {
      sessionStorage.removeItem(STAMP);
    } catch {
      // As above.
    }
  }
}
