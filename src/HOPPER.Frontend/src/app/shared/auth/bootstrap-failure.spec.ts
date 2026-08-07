import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppDto } from '../../api/model/appDto';
import { missingOidcSettings, showBootstrapFailure } from './bootstrap-failure';

function app(overrides: Partial<AppDto>): AppDto {
  return {
    authority: 'http://localhost:58538/default',
    clientId: 'hopper-dashboard',
    redirectUri: 'http://localhost:4200',
    postLogoutRedirectUri: 'http://localhost:4200',
    scope: 'openid profile email',
    version: '1.0.0',
    ...overrides,
  };
}

// Mirrors index.html. Kept here rather than imported so a change to one without the other fails.
const MARKUP = `
  <div id="bootstrap-failure" hidden>
    <h1>HOPPER could not start</h1>
    <p id="bootstrap-failure-detail"></p>
    <button id="bootstrap-failure-retry" type="button">Try again</button>
  </div>
`;

describe('missingOidcSettings', () => {
  it('names every OIDC setting the server left empty, by its environment variable', () => {
    expect(missingOidcSettings(app({ authority: '', redirectUri: '' }))).toEqual([
      'Oidc__Authority',
      'Oidc__RedirectUri',
    ]);
  });

  it('finds nothing missing when the server is fully configured', () => {
    expect(missingOidcSettings(app({}))).toEqual([]);
  });
});

describe('showBootstrapFailure', () => {
  beforeEach(() => {
    document.body.innerHTML = MARKUP;
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  function detail(): string {
    return document.getElementById('bootstrap-failure-detail')?.textContent ?? '';
  }

  it('unhides the static block and writes the unreachable message', () => {
    showBootstrapFailure('unreachable');

    expect(document.getElementById('bootstrap-failure')?.hasAttribute('hidden')).toBe(false);
    expect(detail()).toContain('http://localhost:5170');
    expect(detail()).not.toContain('identity provider');
  });

  it('writes a different message for a server with no identity provider, naming the variables', () => {
    showBootstrapFailure('unconfigured', ['Oidc__Authority', 'Oidc__RedirectUri']);

    expect(detail()).toContain('Oidc__Authority');
    expect(detail()).toContain('Oidc__RedirectUri');
    expect(detail()).not.toContain('http://localhost:5170');
  });

  it('can be called twice without re-wiring the retry button', () => {
    const retry = document.getElementById('bootstrap-failure-retry');
    const listen = vi.spyOn(retry as HTMLElement, 'addEventListener');

    showBootstrapFailure('unreachable');
    showBootstrapFailure('unknown');

    expect(listen).toHaveBeenCalledTimes(1);
    expect(detail()).toContain('Reload the page');
  });
});
