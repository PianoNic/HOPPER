import { Injectable, inject } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { toast } from '@spartan-ng/brain/sonner';

const STAMP = 'hopper.session-recovery';

@Injectable({ providedIn: 'root' })
export class SessionRecovery {
  private readonly oidc = inject(OidcSecurityService);
  private redirecting = false;

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
      return false;
    }
  }

  private stamp(): void {
    try {
      sessionStorage.setItem(STAMP, '1');
    } catch {
    }
  }

  clear(): void {
    try {
      sessionStorage.removeItem(STAMP);
    } catch {
    }
  }
}
