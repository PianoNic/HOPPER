import { ClientDto } from '../../api/model/clientDto';
import { ModDto } from '../../api/model/modDto';
import { ModSide } from '../../api/model/modSide';
import { SyncSide } from '../../api/model/syncSide';
import { toNumber } from './format';

export type ClientDrift = {
  client: ClientDto;

  /// Absent and already on the server when this client last launched, so it synced and still does
  /// not have them. The only one of the two that is a fault.
  missing: ReadonlyArray<ModDto>;

  /// Absent only because they were added after this client last launched. The next launch fetches
  /// them, which is the entire point of HOPPER, so it is not drift.
  behind: ReadonlyArray<ModDto>;

  unknown: number;
  status: 'in sync' | 'behind' | 'drift' | 'offline';
};

export const OFFLINE_AFTER_MS = 24 * 60 * 60 * 1000;

export const OFFLINE_AFTER_LABEL = `${OFFLINE_AFTER_MS / (60 * 60 * 1000)}h`;

export function countDrift(rows: ReadonlyArray<ClientDrift>): { active: number; drifting: number } {
  return {
    active: rows.filter((r) => r.status !== 'offline').length,
    drifting: rows.filter((r) => r.status === 'drift').length,
  };
}

export function reaches(mod: ModDto, side: SyncSide): boolean {
  if (mod.side === ModSide.ClientOnly) return side === SyncSide.Client;
  if (mod.side === ModSide.ServerOnly) return side === SyncSide.Server;
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
    client: sum(mods.filter((m) => reaches(m, SyncSide.Client))),
    server: sum(mods.filter((m) => reaches(m, SyncSide.Server))),
  };
}

export function diffClient(
  client: ClientDto,
  required: ReadonlyArray<ModDto>,
  now: number,
): ClientDrift {
  const reported = new Set(client.mods.map((m) => m.sha256));
  const absent = required.filter((m) => reaches(m, client.side) && !reported.has(m.sha256));

  const unknown = client.mods.filter((m) => !m.known).length;

  const lastSeen = Date.parse(client.lastSeenAt);
  const offline = Number.isNaN(lastSeen) || lastSeen < now - OFFLINE_AFTER_MS;

  // A mod added since the client last launched was never offered to it. Treating that as drift
  // makes every client look broken the moment an admin adds anything.
  const addedSince = (mod: ModDto): boolean => {
    const added = Date.parse(mod.createdAt);
    return !Number.isNaN(added) && !Number.isNaN(lastSeen) && added > lastSeen;
  };

  const behind = absent.filter(addedSince);
  const missing = absent.filter((m) => !addedSince(m));

  return {
    client,
    missing,
    behind,
    unknown,
    status: offline
      ? 'offline'
      : missing.length > 0 || unknown > 0
        ? 'drift'
        : behind.length > 0
          ? 'behind'
          : 'in sync',
  };
}
