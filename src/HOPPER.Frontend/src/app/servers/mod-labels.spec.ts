import { describe, expect, it } from 'vitest';
import {
  MOD_LOADER,
  MOD_SOURCE,
  PLAN_NODE_KIND,
  PLAN_NODE_STATUS,
  formatCount,
  isReplaceable,
  modLoaderFacet,
  modLoaderFromFacet,
  modLoaderLabel,
  modSourceLabel,
  modrinthProjectUrl,
  planNodeKindLabel,
  planNodeStatusDetail,
  planNodeStatusLabel,
  versionTypeLabel,
} from './mod-labels';

describe('modSourceLabel', () => {
  it('names each source', () => {
    expect(modSourceLabel(MOD_SOURCE.manual)).toBe('Uploaded');
    expect(modSourceLabel(MOD_SOURCE.modrinth)).toBe('Modrinth');
    expect(modSourceLabel(MOD_SOURCE.curseForge)).toBe('CurseForge');
  });

  it('does not fall back to the first case', () => {
    expect(modSourceLabel(99)).toBe('Unknown');
  });
});

describe('modLoaderLabel', () => {
  it('names each loader and distinguishes unset from unknown', () => {
    expect(modLoaderLabel(MOD_LOADER.unknown)).toBe('Not set');
    expect(modLoaderLabel(MOD_LOADER.forge)).toBe('Forge');
    expect(modLoaderLabel(MOD_LOADER.neoForge)).toBe('NeoForge');
    expect(modLoaderLabel(MOD_LOADER.fabric)).toBe('Fabric');
    expect(modLoaderLabel(MOD_LOADER.quilt)).toBe('Quilt');
    expect(modLoaderLabel(7)).toBe('Unknown');
  });
});

describe('modLoaderFacet', () => {
  it('uses the names Modrinth knows, lowercase and unspaced', () => {
    expect(modLoaderFacet(MOD_LOADER.forge)).toBe('forge');
    expect(modLoaderFacet(MOD_LOADER.neoForge)).toBe('neoforge');
    expect(modLoaderFacet(MOD_LOADER.fabric)).toBe('fabric');
    expect(modLoaderFacet(MOD_LOADER.quilt)).toBe('quilt');
  });

  it('is null for an unset loader', () => {
    expect(modLoaderFacet(MOD_LOADER.unknown)).toBeNull();
    expect(modLoaderFacet(42)).toBeNull();
  });
});

describe('modLoaderFromFacet', () => {
  it('round-trips every named loader', () => {
    for (const loader of [
      MOD_LOADER.forge,
      MOD_LOADER.neoForge,
      MOD_LOADER.fabric,
      MOD_LOADER.quilt,
    ]) {
      expect(modLoaderFromFacet(modLoaderFacet(loader) as string)).toBe(loader);
    }
  });

  it('accepts the casing Modrinth ships and rejects anything else', () => {
    expect(modLoaderFromFacet('NeoForge')).toBe(MOD_LOADER.neoForge);
    expect(modLoaderFromFacet('rift')).toBe(MOD_LOADER.unknown);
  });
});

describe('plan node labels', () => {
  it('names every kind', () => {
    expect(planNodeKindLabel(PLAN_NODE_KIND.root)).toBe('Selected');
    expect(planNodeKindLabel(PLAN_NODE_KIND.required)).toBe('Required');
    expect(planNodeKindLabel(PLAN_NODE_KIND.optional)).toBe('Optional');
    expect(planNodeKindLabel(9)).toBe('Unknown');
  });

  it('names every status', () => {
    expect(planNodeStatusLabel(PLAN_NODE_STATUS.new)).toBe('New');
    expect(planNodeStatusLabel(PLAN_NODE_STATUS.alreadyInstalled)).toBe('Already installed');
    expect(planNodeStatusLabel(PLAN_NODE_STATUS.otherVersionInstalled)).toBe(
      'Other version installed',
    );
    expect(planNodeStatusLabel(PLAN_NODE_STATUS.fileNameTaken)).toBe('Filename taken');
    expect(planNodeStatusLabel(9)).toBe('Unknown');
  });

  it('explains only the statuses that are not New', () => {
    expect(planNodeStatusDetail(PLAN_NODE_STATUS.new)).toBe('');
    expect(planNodeStatusDetail(PLAN_NODE_STATUS.alreadyInstalled)).toContain('already on');
    expect(planNodeStatusDetail(PLAN_NODE_STATUS.otherVersionInstalled)).toContain('Replace');
  });

  it('offers Replace on exactly the two conflicting statuses', () => {
    expect(isReplaceable(PLAN_NODE_STATUS.new)).toBe(false);
    expect(isReplaceable(PLAN_NODE_STATUS.alreadyInstalled)).toBe(false);
    expect(isReplaceable(PLAN_NODE_STATUS.otherVersionInstalled)).toBe(true);
    expect(isReplaceable(PLAN_NODE_STATUS.fileNameTaken)).toBe(true);
  });
});

describe('versionTypeLabel', () => {
  it('reads the three channels Modrinth publish', () => {
    expect(versionTypeLabel('release')).toBe('Release');
    expect(versionTypeLabel('beta')).toBe('Beta');
    expect(versionTypeLabel('alpha')).toBe('Alpha');
  });

  it('survives a missing or unexpected value', () => {
    expect(versionTypeLabel(null)).toBe('Unknown');
    expect(versionTypeLabel(undefined)).toBe('Unknown');
    expect(versionTypeLabel('nightly')).toBe('Unknown');
  });
});

describe('modrinthProjectUrl', () => {
  it('builds a project link from either identifier', () => {
    expect(modrinthProjectUrl('jei')).toBe('https://modrinth.com/mod/jei');
    expect(modrinthProjectUrl('u6dRKJwZ')).toBe('https://modrinth.com/mod/u6dRKJwZ');
  });

  it('is null without an identifier', () => {
    expect(modrinthProjectUrl(null)).toBeNull();
    expect(modrinthProjectUrl(undefined)).toBeNull();
    expect(modrinthProjectUrl('')).toBeNull();
  });
});

describe('formatCount', () => {
  it('shortens the large numbers Modrinth reports', () => {
    expect(formatCount(0)).toBe('0');
    expect(formatCount(999)).toBe('999');
    expect(formatCount(1500)).toBe('1.5K');
    expect(formatCount(67_575_746)).toBe('68M');
  });
});
