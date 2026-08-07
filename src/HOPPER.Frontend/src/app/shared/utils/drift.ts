import { ClientDto } from '../../api/model/clientDto';
import { ModDto } from '../../api/model/modDto';

export type ClientDrift = {
  client: ClientDto;

  missing: ReadonlyArray<ModDto>;

  unknown: number;
  status: 'in sync' | 'drift' | 'offline';
};

export const OFFLINE_AFTER_MS = 24 * 60 * 60 * 1000;

// Derived, not restated: a label written by hand is the same defect one layer up.
export const OFFLINE_AFTER_LABEL = `${OFFLINE_AFTER_MS / (60 * 60 * 1000)}h`;

// The Overview's two tiles, as one function, because the counting is what #47 found duplicated and
// an inline derivation in a computed cannot be tested on its own.
export function countDrift(rows: ReadonlyArray<ClientDrift>): { active: number; drifting: number } {
  return {
    active: rows.filter((r) => r.status !== 'offline').length,
    drifting: rows.filter((r) => r.status === 'drift').length,
  };
}

export function diffClient(
  client: ClientDto,
  required: ReadonlyArray<ModDto>,
  now: number,
): ClientDrift {
  const reported = new Set(client.mods.map((m) => m.sha256));
  const missing = required.filter((m) => !reported.has(m.sha256));

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
