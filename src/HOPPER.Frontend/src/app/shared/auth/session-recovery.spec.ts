import { TestBed } from '@angular/core/testing';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionRecovery } from './session-recovery';

const authorize = vi.fn();

function recovery(): SessionRecovery {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [{ provide: OidcSecurityService, useValue: { authorize } }],
  });
  return TestBed.inject(SessionRecovery);
}

describe('SessionRecovery', () => {
  beforeEach(() => {
    sessionStorage.clear();
    authorize.mockClear();
  });

  it('redirects once even when a whole forkJoin 401s in the same tick', () => {
    const service = recovery();

    service.recover();
    service.recover();
    service.recover();

    expect(authorize).toHaveBeenCalledTimes(1);
  });

  it('does not redirect a second time after signing in did not help', () => {
    recovery().recover();
    expect(authorize).toHaveBeenCalledTimes(1);

    recovery().recover();
    expect(authorize).toHaveBeenCalledTimes(1);
  });

  it('redirects again once the API has accepted a token since', () => {
    recovery().recover();

    const next = recovery();
    next.clear();
    next.recover();

    expect(authorize).toHaveBeenCalledTimes(2);
  });
});
