import { describe, expect, it } from 'vitest';
import { MOD_SIDE, modSideLabel } from './mod-labels';

describe('modSideLabel', () => {
  it('names each side the way the table shows it', () => {
    expect(modSideLabel(MOD_SIDE.both)).toBe('Both');
    expect(modSideLabel(MOD_SIDE.clientOnly)).toBe('Client only');
    expect(modSideLabel(MOD_SIDE.serverOnly)).toBe('Server only');
  });

  // A value the dashboard does not know about is a server that has moved on, not a crash. Reading
  // it as Both matches what the backend does with an unrecognised side.
  it('falls back to Both for anything unknown', () => {
    expect(modSideLabel(99)).toBe('Both');
  });
});

// The counts in the header, extracted so the arithmetic is testable without a component harness.
export function sideCounts(sides: ReadonlyArray<number>): { clients: number; servers: number } {
  return {
    clients: sides.filter((s) => s !== MOD_SIDE.serverOnly).length,
    servers: sides.filter((s) => s !== MOD_SIDE.clientOnly).length,
  };
}

describe('side counts', () => {
  it('counts a mod on both sides towards both', () => {
    const { clients, servers } = sideCounts([MOD_SIDE.both, MOD_SIDE.both]);

    expect(clients).toBe(2);
    expect(servers).toBe(2);
  });

  it('leaves a one-sided mod out of the other count', () => {
    const { clients, servers } = sideCounts([
      MOD_SIDE.both,
      MOD_SIDE.clientOnly,
      MOD_SIDE.serverOnly,
    ]);

    expect(clients).toBe(2);
    expect(servers).toBe(2);
  });

  it('is zero on both sides for an empty server', () => {
    expect(sideCounts([])).toEqual({ clients: 0, servers: 0 });
  });
});
