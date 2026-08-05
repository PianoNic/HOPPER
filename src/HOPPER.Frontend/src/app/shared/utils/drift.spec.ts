import { describe, expect, it } from 'vitest';
import { diffClient, OFFLINE_AFTER_MS } from './drift';
import { ClientDto } from '../../api/model/clientDto';
import { ClientModDto } from '../../api/model/clientModDto';
import { ModDto } from '../../api/model/modDto';

const NOW = Date.parse('2026-08-05T12:00:00Z');

function mod(fileName: string, sha256: string): ModDto {
  return {
    id: sha256,
    fileName,
    sha256,
    size: {},
    uploadedBy: null,
    createdAt: '2026-08-01T00:00:00Z',
  };
}

function reported(fileName: string, sha256: string, known: boolean): ClientModDto {
  return { fileName, sha256, known };
}

function client(mods: ClientModDto[], lastSeenAt = '2026-08-05T11:59:00Z'): ClientDto {
  return {
    id: 'row-id',
    clientId: 'client-id',
    username: 'steve',
    lastSeenAt,
    lastIpAddress: null,
    mods,
    createdAt: '2026-08-01T00:00:00Z',
  };
}

describe('diffClient', () => {
  it('reports in sync when every required hash is present and nothing extra is', () => {
    const required = [mod('jei.jar', 'aa'), mod('rei.jar', 'bb')];
    const result = diffClient(
      client([reported('jei.jar', 'aa', true), reported('rei.jar', 'bb', true)]),
      required,
      NOW,
    );

    expect(result.status).toBe('in sync');
    expect(result.missing).toHaveLength(0);
    expect(result.unknown).toBe(0);
  });

  it('lists the required mods whose hash the client did not report', () => {
    const required = [mod('jei.jar', 'aa'), mod('rei.jar', 'bb')];
    const result = diffClient(client([reported('jei.jar', 'aa', true)]), required, NOW);

    expect(result.status).toBe('drift');
    expect(result.missing.map((m) => m.fileName)).toEqual(['rei.jar']);
  });

  it('counts reported jars the server does not recognise', () => {
    const required = [mod('jei.jar', 'aa')];
    const result = diffClient(
      client([reported('jei.jar', 'aa', true), reported('cheats.jar', 'zz', false)]),
      required,
      NOW,
    );

    expect(result.status).toBe('drift');
    expect(result.unknown).toBe(1);
  });

  it('matches on hash rather than filename, so a stale jar under the right name is missing', () => {
    const required = [mod('jei.jar', 'new-hash')];
    const result = diffClient(client([reported('jei.jar', 'old-hash', false)]), required, NOW);

    expect(result.missing.map((m) => m.sha256)).toEqual(['new-hash']);
    expect(result.unknown).toBe(1);
  });

  it('reports offline instead of drift once the client has not checked in for a day', () => {
    const required = [mod('jei.jar', 'aa')];
    const stale = new Date(NOW - OFFLINE_AFTER_MS - 1000).toISOString();
    const result = diffClient(client([], stale), required, NOW);

    expect(result.status).toBe('offline');
    // The diff is still computed - the dialog shows it - it just does not drive the badge.
    expect(result.missing).toHaveLength(1);
  });

  it('treats an unparseable last-seen timestamp as offline rather than as up to date', () => {
    const result = diffClient(client([], 'not-a-date'), [], NOW);
    expect(result.status).toBe('offline');
  });
});
