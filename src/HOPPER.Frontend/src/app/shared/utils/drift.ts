import { ClientDto } from '../../api/model/clientDto';
import { ModDto } from '../../api/model/modDto';

/** A client joined with its diff against the required mod set. */
export type ClientDrift = {
  client: ClientDto;
  /** Required jars whose hash the client did not report. */
  missing: ReadonlyArray<ModDto>;
  /** How many reported jars no Mod row matches — i.e. jars we never sent. */
  unknown: number;
  status: 'in sync' | 'drift' | 'offline';
};

/**
 * A client that has not checked in for a day is almost certainly just not playing, so whatever it
 * last reported says nothing useful about whether it is up to date. Report that as "offline"
 * rather than as drift, or the page turns red every time someone takes a week off.
 */
export const OFFLINE_AFTER_MS = 24 * 60 * 60 * 1000;

/**
 * Diffs one client's reported jars against the required set. Kept as a pure function of
 * (client, required, now) so the page can recompute it inside a `computed()` and so the status
 * rules are testable without standing up a component.
 *
 * The comparison is on sha256, not filename: a jar renamed on the server is a different manifest
 * entry, and a client carrying the old name still has the right bytes only if the hash matches.
 */
export function diffClient(
  client: ClientDto,
  required: ReadonlyArray<ModDto>,
  now: number,
): ClientDrift {
  const reported = new Set(client.mods.map((m) => m.sha256));
  const missing = required.filter((m) => !reported.has(m.sha256));

  // `known` is the server's own answer to "does any Mod row carry this hash", so an unknown jar is
  // one the player dropped in by hand or one left over from a mod that has since been deleted.
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
