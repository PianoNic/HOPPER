import { describe, expect, it } from 'vitest';
import { formatCount, isReplaceable, modLoaderFacet, modLoaderFromFacet, modLoaderLabel, modSourceLabel, modrinthProjectUrl, planNodeKindLabel, planNodeStatusDetail, planNodeStatusLabel, versionTypeLabel } from './mod-labels';
import { ModLoader } from '../api/model/modLoader';
import { ModSource } from '../api/model/modSource';
import { PlanNodeKind } from '../api/model/planNodeKind';
import { PlanNodeStatus } from '../api/model/planNodeStatus';

describe('modSourceLabel', () => {
  it('names each source', () => {
    expect(modSourceLabel(ModSource.Manual)).toBe('Uploaded');
    expect(modSourceLabel(ModSource.Modrinth)).toBe('Modrinth');
    expect(modSourceLabel(ModSource.CurseForge)).toBe('CurseForge');
  });

  it('does not fall back to the first case', () => {
    expect(modSourceLabel(99)).toBe('Unknown');
  });
});

describe('modLoaderLabel', () => {
  it('names each loader and distinguishes unset from unknown', () => {
    expect(modLoaderLabel(ModLoader.Unknown)).toBe('Not set');
    expect(modLoaderLabel(ModLoader.Forge)).toBe('Forge');
    expect(modLoaderLabel(ModLoader.NeoForge)).toBe('NeoForge');
    expect(modLoaderLabel(ModLoader.Fabric)).toBe('Fabric');
    expect(modLoaderLabel(ModLoader.Quilt)).toBe('Quilt');
    expect(modLoaderLabel(7)).toBe('Unknown');
  });
});

describe('modLoaderFacet', () => {
  it('uses the names Modrinth knows, lowercase and unspaced', () => {
    expect(modLoaderFacet(ModLoader.Forge)).toBe('forge');
    expect(modLoaderFacet(ModLoader.NeoForge)).toBe('neoforge');
    expect(modLoaderFacet(ModLoader.Fabric)).toBe('fabric');
    expect(modLoaderFacet(ModLoader.Quilt)).toBe('quilt');
  });

  it('is null for an unset loader', () => {
    expect(modLoaderFacet(ModLoader.Unknown)).toBeNull();
    expect(modLoaderFacet(42)).toBeNull();
  });
});

describe('modLoaderFromFacet', () => {
  it('round-trips every named loader', () => {
    for (const loader of [
      ModLoader.Forge,
      ModLoader.NeoForge,
      ModLoader.Fabric,
      ModLoader.Quilt,
    ]) {
      expect(modLoaderFromFacet(modLoaderFacet(loader) as string)).toBe(loader);
    }
  });

  it('accepts the casing Modrinth ships and rejects anything else', () => {
    expect(modLoaderFromFacet('NeoForge')).toBe(ModLoader.NeoForge);
    expect(modLoaderFromFacet('rift')).toBe(ModLoader.Unknown);
  });
});

describe('plan node labels', () => {
  it('names every kind', () => {
    expect(planNodeKindLabel(PlanNodeKind.Root)).toBe('Selected');
    expect(planNodeKindLabel(PlanNodeKind.Required)).toBe('Required');
    expect(planNodeKindLabel(PlanNodeKind.Optional)).toBe('Optional');
    expect(planNodeKindLabel(9)).toBe('Unknown');
  });

  it('names every status', () => {
    expect(planNodeStatusLabel(PlanNodeStatus.New)).toBe('New');
    expect(planNodeStatusLabel(PlanNodeStatus.AlreadyInstalled)).toBe('Already installed');
    expect(planNodeStatusLabel(PlanNodeStatus.OtherVersionInstalled)).toBe(
      'Other version installed',
    );
    expect(planNodeStatusLabel(PlanNodeStatus.FileNameTaken)).toBe('Filename taken');
    expect(planNodeStatusLabel(9)).toBe('Unknown');
  });

  it('explains only the statuses that are not New', () => {
    expect(planNodeStatusDetail(PlanNodeStatus.New)).toBe('');
    expect(planNodeStatusDetail(PlanNodeStatus.AlreadyInstalled)).toContain('already on');
    expect(planNodeStatusDetail(PlanNodeStatus.OtherVersionInstalled)).toContain('Replace');
  });

  it('offers Replace on exactly the two conflicting statuses', () => {
    expect(isReplaceable(PlanNodeStatus.New)).toBe(false);
    expect(isReplaceable(PlanNodeStatus.AlreadyInstalled)).toBe(false);
    expect(isReplaceable(PlanNodeStatus.OtherVersionInstalled)).toBe(true);
    expect(isReplaceable(PlanNodeStatus.FileNameTaken)).toBe(true);
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
