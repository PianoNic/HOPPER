import { ClientDto } from '../../api/model/clientDto';
import { ModDto } from '../../api/model/modDto';

export type ClientDrift = {
  client: ClientDto;

  missing: ReadonlyArray<ModDto>;

  unknown: number;
  status: 'in sync' | 'drift' | 'offline';
};

export const OFFLINE_AFTER_MS = 24 * 60 * 60 * 1000;

// Kept beside the constant so a tile that names the window in its label cannot drift from the rule.
export const OFFLINE_AFTER_LABEL = '24h';

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
