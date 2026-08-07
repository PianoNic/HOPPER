import { ClientDto } from '../../api/model/clientDto';
import { ModDto } from '../../api/model/modDto';
import { MOD_SIDE, SYNC_SIDE } from '../../servers/mod-labels';
import { toNumber } from './format';

export type ClientDrift = {
  client: ClientDto;

  missing: ReadonlyArray<ModDto>;

  unknown: number;
  status: 'in sync' | 'drift' | 'offline';
};

export const OFFLINE_AFTER_MS = 24 * 60 * 60 * 1000;

export const OFFLINE_AFTER_LABEL = `${OFFLINE_AFTER_MS / (60 * 60 * 1000)}h`;

export function countDrift(rows: ReadonlyArray<ClientDrift>): { active: number; drifting: number } {
  return {
    active: rows.filter((r) => r.status !== 'offline').length,
    drifting: rows.filter((r) => r.status === 'drift').length,
  };
}

export function reaches(mod: ModDto, side: number): boolean {
  if (mod.side === MOD_SIDE.clientOnly) return side === SYNC_SIDE.client;
  if (mod.side === MOD_SIDE.serverOnly) return side === SYNC_SIDE.server;
  return true;
}

export function downloadSizes(mods: ReadonlyArray<ModDto>): {
  stored: number;
  client: number;
  server: number;
} {
  const sum = (kept: ReadonlyArray<ModDto>): number => {
    const seen = new Set<string>();
    let total = 0;

    for (const mod of kept) {
      if (seen.has(mod.sha256)) continue;

      seen.add(mod.sha256);
      total += toNumber(mod.size);
    }

    return total;
  };

  return {
    stored: sum(mods),
    client: sum(mods.filter((m) => reaches(m, SYNC_SIDE.client))),
    server: sum(mods.filter((m) => reaches(m, SYNC_SIDE.server))),
  };
}

export function diffClient(
  client: ClientDto,
  required: ReadonlyArray<ModDto>,
  now: number,
): ClientDrift {
  const reported = new Set(client.mods.map((m) => m.sha256));
  const missing = required.filter((m) => reaches(m, client.side) && !reported.has(m.sha256));

  const unknown = client.mods.filter((m) => !m.known).length;

  const lastSeen = Date.parse(client.lastSeenAt);
  const offline = Number.isNaN(lastSeen) || lastSeen < now - OFFLINE_AFTER_MS;

  return {
    client,
    missing,
    unknown,
    status: offline ? 'offline' : missing.length === 0 && unknown === 0 ? 'in sync' : 'drift',
  };
}
