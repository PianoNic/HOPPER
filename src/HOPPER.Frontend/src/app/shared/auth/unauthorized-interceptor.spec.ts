import { describe, expect, it } from 'vitest';
import { isApiRequest } from './unauthorized-interceptor';

describe('isApiRequest', () => {
  it('treats a 401 from the HOPPER API as a dead session', () => {
    expect(isApiRequest('http://localhost:5170/api/servers')).toBe(true);
    expect(isApiRequest('http://localhost:5170/api/servers/1/clients')).toBe(true);
  });

  it('ignores a 401 from the identity provider, so a token-endpoint failure cannot start a redirect fight', () => {
    expect(isApiRequest('http://localhost:58538/default/token')).toBe(false);
    expect(
      isApiRequest('https://keycloak.example/realms/hopper/protocol/openid-connect/token'),
    ).toBe(false);
  });
});
