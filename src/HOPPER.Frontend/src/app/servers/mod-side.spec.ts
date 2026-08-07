import { describe, expect, it } from 'vitest';
import { modSideLabel } from './mod-labels';
import { ModSide } from '../api/model/modSide';

describe('modSideLabel', () => {
  it('names each side the way the table shows it', () => {
    expect(modSideLabel(ModSide.Both)).toBe('Both');
    expect(modSideLabel(ModSide.ClientOnly)).toBe('Client only');
    expect(modSideLabel(ModSide.ServerOnly)).toBe('Server only');
  });

  it('falls back to Both for anything unknown', () => {
    expect(modSideLabel(99)).toBe('Both');
  });
});

export function sideCounts(sides: ReadonlyArray<number>): { clients: number; servers: number } {
  return {
    clients: sides.filter((s) => s !== ModSide.ServerOnly).length,
    servers: sides.filter((s) => s !== ModSide.ClientOnly).length,
  };
}

describe('side counts', () => {
  it('counts a mod on both sides towards both', () => {
    const { clients, servers } = sideCounts([ModSide.Both, ModSide.Both]);

    expect(clients).toBe(2);
    expect(servers).toBe(2);
  });

  it('leaves a one-sided mod out of the other count', () => {
    const { clients, servers } = sideCounts([
      ModSide.Both,
      ModSide.ClientOnly,
      ModSide.ServerOnly,
    ]);

    expect(clients).toBe(2);
    expect(servers).toBe(2);
  });

  it('is zero on both sides for an empty server', () => {
    expect(sideCounts([])).toEqual({ clients: 0, servers: 0 });
  });
});
