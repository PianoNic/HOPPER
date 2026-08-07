import { ModDto } from '../api/model/modDto';
import { toNumber } from '../shared/utils/format';
import { PackFormat } from '../api/model/packFormat';
import { ModSource } from '../api/model/modSource';
import { SyncSide } from '../api/model/syncSide';
import { reaches } from '../shared/utils/drift';

export interface PackSplit {
  readonly manifestEntries: number;

  readonly bundledFiles: number;

  readonly bundledBytes: number;

  readonly withheld: number;
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

export function packSplit(mods: ReadonlyArray<ModDto>, format: PackFormat): PackSplit {
  let manifestEntries = 0;
  let bundledFiles = 0;
  let bundledBytes = 0;

  let withheld = 0;

  for (const mod of mods) {
    if (!carries(mod, format)) {
      withheld += 1;
      continue;
    }

    if (linksInFormat(mod, format)) {
      manifestEntries += 1;
    } else {
      bundledFiles += 1;
      bundledBytes += toNumber(mod.size);
    }
  }

  return { manifestEntries, bundledFiles, bundledBytes, withheld };
}

// A Prism instance is one machine's game directory rather than a distributable, and in practice a
// client one, so a server-only jar dropped into its mods/ folder is a jar the game will load and
// should not. The other two formats are packs a server operator installs too, so they carry both.
function carries(mod: ModDto, format: PackFormat): boolean {
  return format !== PackFormat.PrismInstance || reaches(mod, SyncSide.Client);
}

function linksInFormat(mod: ModDto, format: PackFormat): boolean {
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
