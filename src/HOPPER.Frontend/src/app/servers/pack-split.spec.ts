import { describe, expect, it } from 'vitest';
import { ModDto } from '../api/model/modDto';
import { ManifestModDtoSize } from '../api/model/manifestModDtoSize';
import { PACK_FORMAT } from './import-labels';
import { MOD_SOURCE } from './mod-labels';
import { linksToCurseForge, linksToModrinth, packSplit } from './pack-split';

function mod(overrides: Partial<ModDto> & { size?: number }): ModDto {
  const { size, ...rest } = overrides;
  return {
    id: 'row',
    fileName: 'a.jar',
    sha256: 'f'.repeat(64),
    size: (size ?? 0) as unknown as ManifestModDtoSize,
    createdAt: '2026-08-06T00:00:00Z',
    source: MOD_SOURCE.manual,
    ...rest,
  } as ModDto;
}

const modrinthMod = mod({
  source: MOD_SOURCE.modrinth,
  projectId: 'u6dRKJwZ',
  versionId: 'mcC2LhSG',
  downloadUrl: 'https://cdn.modrinth.com/data/u6dRKJwZ/versions/mcC2LhSG/jei.jar',
  size: 1_000_000,
});

describe('linksToModrinth', () => {
  it('accepts a row carrying a project, a version and a URL', () => {
    expect(linksToModrinth(modrinthMod)).toBe(true);
  });

  // The exporters test provenance rather than Source, because a row whose URL is missing would
  // otherwise become a manifest entry pointing at nothing. The dialog has to predict the same way
  // or its count is wrong exactly when the pack is at its most surprising.
  it('rejects a Modrinth row with a field missing', () => {
    expect(linksToModrinth({ ...modrinthMod, downloadUrl: null })).toBe(false);
    expect(linksToModrinth({ ...modrinthMod, versionId: '' })).toBe(false);
    expect(linksToModrinth({ ...modrinthMod, projectId: '   ' })).toBe(false);
  });

  it('rejects a hand-uploaded row', () => {
    expect(linksToModrinth(mod({ size: 10 }))).toBe(false);
  });
});

describe('linksToCurseForge', () => {
  it('accepts only numeric ids, which is what a CurseForge files[] entry is made of', () => {
    expect(
      linksToCurseForge(
        mod({ source: MOD_SOURCE.curseForge, projectId: '32274', versionId: '5172461' }),
      ),
    ).toBe(true);
  });

  // The guard that matters: base62 project ids can never parse as CurseForge integers, so a
  // Modrinth mod cannot slip into a manifest that would then address the wrong file entirely.
  it('never accepts a Modrinth row', () => {
    expect(linksToCurseForge(modrinthMod)).toBe(false);
    expect(linksToCurseForge({ ...modrinthMod, source: MOD_SOURCE.curseForge })).toBe(false);
  });
});

describe('packSplit', () => {
  const mods = [
    modrinthMod,
    { ...modrinthMod, id: 'second', fileName: 'b.jar' },
    mod({ id: 'manual', fileName: 'handmade.jar', size: 4_000_000 }),
  ];

  it('links Modrinth rows in a mrpack and bundles only the rest', () => {
    expect(packSplit(mods, PACK_FORMAT.modrinth)).toEqual({
      manifestEntries: 2,
      bundledFiles: 1,
      bundledBytes: 4_000_000,
    });
  });

  // The point of quoting a size per format: the same three mods are a 4 MB download as a mrpack and
  // a 6 MB one as a CurseForge zip, and nothing in the format's name says so.
  it('bundles everything in a CurseForge pack while nothing records CurseForge ids', () => {
    expect(packSplit(mods, PACK_FORMAT.curseForge)).toEqual({
      manifestEntries: 0,
      bundledFiles: 3,
      bundledBytes: 6_000_000,
    });
  });

  it('bundles everything in a Prism instance, which has no manifest at all', () => {
    expect(packSplit(mods, PACK_FORMAT.prismInstance)).toEqual({
      manifestEntries: 0,
      bundledFiles: 3,
      bundledBytes: 6_000_000,
    });
  });

  it('quotes the worst case for a format it does not know', () => {
    expect(packSplit(mods, 99).bundledFiles).toBe(3);
  });

  it('answers zero for a server with no mods', () => {
    expect(packSplit([], PACK_FORMAT.modrinth)).toEqual({
      manifestEntries: 0,
      bundledFiles: 0,
      bundledBytes: 0,
    });
  });
});
