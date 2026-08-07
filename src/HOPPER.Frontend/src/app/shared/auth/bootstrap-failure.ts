import { AppDto } from '../../api/model/appDto';
import { environment } from '../environments/environment';

export type BootstrapFailureKind = 'unreachable' | 'unconfigured' | 'unknown';

const SETTING_VARIABLES = [
  ['authority', 'Oidc__Authority'],
  ['clientId', 'Oidc__ClientId'],
  ['redirectUri', 'Oidc__RedirectUri'],
] as const satisfies ReadonlyArray<readonly [keyof AppDto, string]>;

export class UnconfiguredOidcError extends Error {
  constructor(readonly missing: ReadonlyArray<string>) {
    super(`The server left these OIDC settings empty: ${missing.join(', ')}`);
    this.name = 'UnconfiguredOidcError';
  }
}

export function missingOidcSettings(app: AppDto): ReadonlyArray<string> {
  return SETTING_VARIABLES.filter(([key]) => (app[key] ?? '') === '').map(([, name]) => name);
}

export function bootstrapFailureMessage(
  kind: BootstrapFailureKind,
  missing: ReadonlyArray<string>,
): string {
  if (kind === 'unreachable') {
    return (
      `HOPPER could not reach its own API at ${environment.apiBaseUrl}. The server may still be ` +
      'starting, or it may be down. Nothing is lost - try again once it is back.'
    );
  }

  if (kind === 'unconfigured') {
    const one = missing.length === 1;
    const names = missing.join(' and ');
    return (
      'HOPPER is running but has no identity provider configured. ' +
      `${names} ${one ? 'is' : 'are'} not set on the server. Set ${one ? 'it' : 'them'} and ` +
      'restart it.'
    );
  }

  return (
    'HOPPER could not start. Reload the page - if it keeps happening, the browser console and ' +
    'the server log will say why.'
  );
}

export function showBootstrapFailure(
  kind: BootstrapFailureKind,
  missing: ReadonlyArray<string> = [],
): void {
  const block = document.getElementById('bootstrap-failure');
  if (block === null) return;

  const detail = document.getElementById('bootstrap-failure-detail');
  if (detail !== null) detail.textContent = bootstrapFailureMessage(kind, missing);

  const retry = document.getElementById('bootstrap-failure-retry');
  if (retry !== null && retry.dataset['wired'] !== 'true') {
    retry.dataset['wired'] = 'true';
    retry.addEventListener('click', () => window.location.reload());
  }

  block.removeAttribute('hidden');
}
