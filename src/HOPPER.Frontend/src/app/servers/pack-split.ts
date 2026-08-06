import { ModDto } from '../api/model/modDto';
import { toNumber } from '../shared/utils/format';
import { PACK_FORMAT } from './import-labels';
import { MOD_SOURCE } from './mod-labels';

export interface PackSplit {
  readonly manifestEntries: number;

  readonly bundledFiles: number;

  readonly bundledBytes: number;
}

export function linksToModrinth(mod: ModDto): boolean {
  return (
    mod.source === MOD_SOURCE.modrinth &&
    notEmpty(mod.projectId) &&
    notEmpty(mod.versionId) &&
    notEmpty(mod.downloadUrl)
  );
}

export function linksToCurseForge(mod: ModDto): boolean {
  return (
    mod.source === MOD_SOURCE.curseForge && isInteger(mod.projectId) && isInteger(mod.versionId)
  );
}

export function packSplit(mods: ReadonlyArray<ModDto>, format: number): PackSplit {
  let manifestEntries = 0;
  let bundledFiles = 0;
  let bundledBytes = 0;

  for (const mod of mods) {
    if (linksInFormat(mod, format)) {
      manifestEntries += 1;
    } else {
      bundledFiles += 1;
      bundledBytes += toNumber(mod.size);
    }
  }

  return { manifestEntries, bundledFiles, bundledBytes };
}

function linksInFormat(mod: ModDto, format: number): boolean {
  switch (format) {
    case PACK_FORMAT.modrinth:
      return linksToModrinth(mod);
    case PACK_FORMAT.curseForge:
      return linksToCurseForge(mod);
    default:

      return false;
  }
}

function notEmpty(value: string | null | undefined): boolean {
  return typeof value === 'string' && value.trim() !== '';
}

function isInteger(value: string | null | undefined): boolean {
  return typeof value === 'string' && /^\d+$/.test(value);
}
