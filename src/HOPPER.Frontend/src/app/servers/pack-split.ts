import { ModDto } from '../api/model/modDto';
import { toNumber } from '../shared/utils/format';
import { PackFormat } from '../api/model/packFormat';
import { ModSource } from '../api/model/modSource';

export interface PackSplit {
  readonly manifestEntries: number;

  readonly bundledFiles: number;

  readonly bundledBytes: number;
}

export function linksToModrinth(mod: ModDto): boolean {
  return (
    mod.source === ModSource.Modrinth &&
    notEmpty(mod.projectId) &&
    notEmpty(mod.versionId) &&
    notEmpty(mod.downloadUrl)
  );
}

export function linksToCurseForge(mod: ModDto): boolean {
  return (
    mod.source === ModSource.CurseForge && isInteger(mod.projectId) && isInteger(mod.versionId)
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
    case PackFormat.Modrinth:
      return linksToModrinth(mod);
    case PackFormat.CurseForge:
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
