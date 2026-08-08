import { describe, expect, it } from 'vitest';
import {
  countDrift,
  diffClient,
  downloadSizes,
  OFFLINE_AFTER_LABEL,
  OFFLINE_AFTER_MS,
  reaches,
} from './drift';
import { ClientDto } from '../../api/model/clientDto';
import { ClientModDto } from '../../api/model/clientModDto';
import { ModDto } from '../../api/model/modDto';
import { ModSide } from '../../api/model/modSide';
import { ModSource } from '../../api/model/modSource';
import { SyncSide } from '../../api/model/syncSide';

const NOW = Date.parse('2026-08-05T12:00:00Z');

function mod(fileName: string, sha256: string): ModDto {
  return {
    id: sha256,
    fileName,
    sha256,
    size: {},
    uploadedBy: null,
    createdAt: '2026-08-01T00:00:00Z',
    source: ModSource.Manual,
    side: ModSide.Both,
  };
}

function reported(fileName: string, sha256: string, known: boolean): ClientModDto {
  return { fileName, sha256, known };
}

function client(
  mods: ClientModDto[],
  lastSeenAt = '2026-08-05T11:59:00Z',
  side: SyncSide = SyncSide.Client,
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

function sidedMod(fileName: string, sha256: string, side: ModSide): ModDto {
  return { ...mod(fileName, sha256), side };
}

function sizedMod(fileName: string, sha256: string, side: ModSide, size: number): ModDto {
  return { ...sidedMod(fileName, sha256, side), size: size as unknown as object };
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

describe('a client that has not launched since mods were added', () => {
  function addedAt(fileName: string, sha256: string, createdAt: string): ModDto {
    return { ...mod(fileName, sha256), createdAt };
  }

  it('is behind rather than drifting, because the next launch fetches them', () => {
    // Added at 11:00 on the 5th; this client last launched at 10:00 the same morning.
    const required = [addedAt('new.jar', 'sha-new', '2026-08-05T11:00:00Z')];
    const row = diffClient(client([], '2026-08-05T10:00:00Z'), required, NOW);

    expect(row.status).toBe('behind');
    expect(row.behind).toHaveLength(1);
    expect(row.missing).toHaveLength(0);
  });

  it('still drifts for a mod that was already there when it last launched', () => {
    const required = [addedAt('old.jar', 'sha-old', '2026-08-01T00:00:00Z')];
    const row = diffClient(client([], '2026-08-05T10:00:00Z'), required, NOW);

    expect(row.status).toBe('drift');
    expect(row.missing).toHaveLength(1);
    expect(row.behind).toHaveLength(0);
  });

  it('drifts when even one absent mod predates the last launch', () => {
    const required = [
      addedAt('old.jar', 'sha-old', '2026-08-01T00:00:00Z'),
      addedAt('new.jar', 'sha-new', '2026-08-05T11:00:00Z'),
    ];

    const row = diffClient(client([], '2026-08-05T10:00:00Z'), required, NOW);

    expect(row.status).toBe('drift');
    expect(row.missing.map((m) => m.fileName)).toEqual(['old.jar']);
    expect(row.behind.map((m) => m.fileName)).toEqual(['new.jar']);
  });

  it('is not counted as drifting on the Overview', () => {
    const required = [addedAt('new.jar', 'sha-new', '2026-08-05T11:00:00Z')];
    const row = diffClient(client([], '2026-08-05T10:00:00Z'), required, NOW);

    expect(countDrift([row])).toEqual({ active: 1, drifting: 0 });
  });

  it('reports not launched before anything else, however far behind it is', () => {
    const required = [addedAt('new.jar', 'sha-new', '2026-08-05T11:00:00Z')];
    const longAgo = new Date(NOW - OFFLINE_AFTER_MS - 1000).toISOString();

    expect(diffClient(client([], longAgo), required, NOW).status).toBe('offline');
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
  it('does not fault a server for the mods only clients get', () => {
    const required = [
      sidedMod('both.jar', 'aaa', ModSide.Both),
      sidedMod('client-only.jar', 'bbb', ModSide.ClientOnly),
      sidedMod('server-only.jar', 'ccc', ModSide.ServerOnly),
    ];
    const server = client(
      [reported('both.jar', 'aaa', true), reported('server-only.jar', 'ccc', true)],
      '2026-08-05T11:59:00Z',
      SyncSide.Server,
    );

    const drift = diffClient(server, required, NOW);

    expect(drift.missing).toEqual([]);
    expect(drift.status).toBe('in sync');
  });

  it('does not fault a player for the mods only servers get', () => {
    const required = [
      sidedMod('both.jar', 'aaa', ModSide.Both),
      sidedMod('server-only.jar', 'ccc', ModSide.ServerOnly),
    ];
    const player = client([reported('both.jar', 'aaa', true)]);

    expect(diffClient(player, required, NOW).status).toBe('in sync');
  });

  it('still reports a mod the client should have and does not', () => {
    const required = [
      sidedMod('both.jar', 'aaa', ModSide.Both),
      sidedMod('client-only.jar', 'bbb', ModSide.ClientOnly),
    ];
    const player = client([reported('both.jar', 'aaa', true)]);

    const drift = diffClient(player, required, NOW);

    expect(drift.missing.map((m) => m.fileName)).toEqual(['client-only.jar']);
    expect(drift.status).toBe('drift');
  });
});

describe('what the Clients page counts', () => {
  const required = [
    sidedMod('both.jar', 'aaa', ModSide.Both),
    sidedMod('client-only.jar', 'bbb', ModSide.ClientOnly),
    sidedMod('server-only.jar', 'ccc', ModSide.ServerOnly),
  ];

  const requiredFor = (side: SyncSide) => required.filter((m) => reaches(m, side)).length;

  it('counts only what that side is sent as the denominator', () => {
    expect(requiredFor(SyncSide.Client)).toBe(2);
    expect(requiredFor(SyncSide.Server)).toBe(2);
  });

  it('agrees with the missing badge', () => {
    const holder = client(
      [reported('both.jar', 'aaa', true), reported('server-only.jar', 'ccc', true)],
      '2026-08-05T11:59:00Z',
      SyncSide.Client,
    );

    const drift = diffClient(holder, required, NOW);
    const matched = requiredFor(SyncSide.Client) - drift.missing.length;

    expect(drift.missing.map((m) => m.fileName)).toEqual(['client-only.jar']);
    expect(matched).toBe(1);
  });
});

describe('downloadSizes', () => {
  it('gives both sides the whole set when nothing is one-sided', () => {
    const sizes = downloadSizes([
      sizedMod('jade.jar', 'aa', ModSide.Both, 1000),
      sizedMod('sodium.jar', 'bb', ModSide.Both, 500),
    ]);

    expect(sizes).toEqual({ stored: 1500, client: 1500, server: 1500 });
  });

  it('leaves a one-sided mod out of the other side', () => {
    const sizes = downloadSizes([
      sizedMod('jade.jar', 'aa', ModSide.Both, 1000),
      sizedMod('jei.jar', 'bb', ModSide.ClientOnly, 300),
      sizedMod('appleskin.jar', 'cc', ModSide.ServerOnly, 70),
    ]);

    expect(sizes).toEqual({ stored: 1370, client: 1300, server: 1070 });
  });

  it('counts a hash once however many mods share it', () => {
    const sizes = downloadSizes([
      sizedMod('jade.jar', 'aa', ModSide.Both, 1000),
      sizedMod('jade-copy.jar', 'aa', ModSide.Both, 1000),
    ]);

    expect(sizes).toEqual({ stored: 1000, client: 1000, server: 1000 });
  });

  it('is zero everywhere for a server with no mods', () => {
    expect(downloadSizes([])).toEqual({ stored: 0, client: 0, server: 0 });
  });
});
