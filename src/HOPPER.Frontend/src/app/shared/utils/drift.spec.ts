import { describe, expect, it } from 'vitest';
import { countDrift, diffClient, OFFLINE_AFTER_LABEL, OFFLINE_AFTER_MS } from './drift';
import { ClientDto } from '../../api/model/clientDto';
import { ClientModDto } from '../../api/model/clientModDto';
import { ModDto } from '../../api/model/modDto';
import { MOD_SIDE, MOD_SOURCE, SYNC_SIDE } from '../../servers/mod-labels';

const NOW = Date.parse('2026-08-05T12:00:00Z');

function mod(fileName: string, sha256: string): ModDto {
  return {
    id: sha256,
    fileName,
    sha256,
    size: {},
    uploadedBy: null,
    createdAt: '2026-08-01T00:00:00Z',
    source: MOD_SOURCE.manual,
    side: MOD_SIDE.both,
  };
}

function reported(fileName: string, sha256: string, known: boolean): ClientModDto {
  return { fileName, sha256, known };
}

function client(
  mods: ClientModDto[],
  lastSeenAt = '2026-08-05T11:59:00Z',
  side: number = SYNC_SIDE.client,
): ClientDto {
  return {
    id: 'row-id',
    clientId: 'client-id',
    username: 'steve',
    side,
    lastSeenAt,
    lastIpAddress: null,
    mods,
    createdAt: '2026-08-01T00:00:00Z',
  };
}

function sidedMod(fileName: string, sha256: string, side: number): ModDto {
  return { ...mod(fileName, sha256), side };
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

    expect(result.missing).toHaveLength(1);
  });

  it('treats an unparseable last-seen timestamp as offline rather than as up to date', () => {
    const result = diffClient(client([], 'not-a-date'), [], NOW);
    expect(result.status).toBe('offline');
  });

  it('never reports a client as drifting once it is offline, even with jars missing', () => {
    const required = [mod('jei.jar', 'aa'), mod('rei.jar', 'bb')];
    const stale = new Date(NOW - OFFLINE_AFTER_MS - 1000).toISOString();
    const result = diffClient(client([reported('cheats.jar', 'zz', false)], stale), required, NOW);

    expect(result.missing).toHaveLength(2);
    expect(result.unknown).toBe(1);
    expect(result.status !== 'drift').toBe(true);
  });
});

describe('countDrift', () => {
  const required = [mod('jei.jar', 'aa')];
  const recent = new Date(NOW - OFFLINE_AFTER_MS + 1000).toISOString();
  const stale = new Date(NOW - OFFLINE_AFTER_MS - 1000).toISOString();

  it('counts what the Overview tiles show', () => {
    const rows = [
      diffClient(client([reported('jei.jar', 'aa', true)], recent), required, NOW),
      diffClient(client([], recent), required, NOW),
      diffClient(client([], stale), required, NOW),
      diffClient(client([], 'not-a-date'), required, NOW),
    ];

    expect(countDrift(rows)).toEqual({ active: 2, drifting: 1 });
  });

  it('counts nothing when no client has ever reported', () => {
    expect(countDrift([])).toEqual({ active: 0, drifting: 0 });
  });

  it('names the same window it counts', () => {
    expect(OFFLINE_AFTER_LABEL).toBe(`${OFFLINE_AFTER_MS / 3600000}h`);
  });
});

describe('diffClient and sides', () => {
  // Measured against a real dedicated server before this was fixed: it reported 2 of 3 mods, was
  // marked missing the client-only jar it was deliberately never sent, and showed DRIFT.
  it('does not fault a server for the mods only clients get', () => {
    const required = [
      sidedMod('both.jar', 'aaa', MOD_SIDE.both),
      sidedMod('client-only.jar', 'bbb', MOD_SIDE.clientOnly),
      sidedMod('server-only.jar', 'ccc', MOD_SIDE.serverOnly),
    ];
    const server = client(
      [reported('both.jar', 'aaa', true), reported('server-only.jar', 'ccc', true)],
      '2026-08-05T11:59:00Z',
      SYNC_SIDE.server,
    );

    const drift = diffClient(server, required, NOW);

    expect(drift.missing).toEqual([]);
    expect(drift.status).toBe('in sync');
  });

  it('does not fault a player for the mods only servers get', () => {
    const required = [
      sidedMod('both.jar', 'aaa', MOD_SIDE.both),
      sidedMod('server-only.jar', 'ccc', MOD_SIDE.serverOnly),
    ];
    const player = client([reported('both.jar', 'aaa', true)]);

    expect(diffClient(player, required, NOW).status).toBe('in sync');
  });

  it('still reports a mod the client should have and does not', () => {
    const required = [
      sidedMod('both.jar', 'aaa', MOD_SIDE.both),
      sidedMod('client-only.jar', 'bbb', MOD_SIDE.clientOnly),
    ];
    const player = client([reported('both.jar', 'aaa', true)]);

    const drift = diffClient(player, required, NOW);

    expect(drift.missing.map((m) => m.fileName)).toEqual(['client-only.jar']);
    expect(drift.status).toBe('drift');
  });
});
