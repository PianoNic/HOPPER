// What each export format does with a server's mods, worked out on the dashboard so the export
// dialog can say it before the download starts rather than after.
//
// The rule all three writers share: a mod whose upstream origin HOPPER recorded becomes a manifest
// entry - one line naming the real CDN URL and the hashes that CDN published, weighing nothing -
// and every other mod is copied into the archive as bytes. That single split is the difference
// between a 40 KB file and a 400 MB one, it is invisible from the format's name, and it is the one
// number an admin needs before pressing download.
//
// Kept free of Angular so it can be tested as what it is: a pure classification.

import { ModDto } from '../api/model/modDto';
import { toNumber } from '../shared/utils/format';
import { PACK_FORMAT } from './import-labels';
import { MOD_SOURCE } from './mod-labels';

/** How one format divides a server's mods. Counts of rows, plus the bytes that actually travel. */
export interface PackSplit {
  /** Mods written as a manifest line pointing at their upstream URL. These cost no bytes. */
  readonly manifestEntries: number;
  /** Mods copied into the archive. These are the download. */
  readonly bundledFiles: number;
  /** Sum of the bundled jars' sizes. The archive is a little smaller - jars are already
   *  compressed, so deflate saves single-digit percent - never larger. */
  readonly bundledBytes: number;
}

/**
 * Whether a .mrpack can reference this jar instead of carrying it.
 *
 * This mirrors ModProvenance.HasModrinthProvenance on the server, minus two fields the dashboard is
 * never sent: the sha1 and sha512 Modrinth published are stored, but nothing renders them, so they
 * are deliberately absent from ModDto. Every writer of Modrinth provenance fills all six columns in
 * one insert, so a row carrying the four visible ones carries the two hidden ones too - but the
 * server remains the authority, which is why the dialog calls its numbers an estimate and why the
 * finished pack reports its own split back in a warning header.
 */
export function linksToModrinth(mod: ModDto): boolean {
  return (
    mod.source === MOD_SOURCE.modrinth &&
    notEmpty(mod.projectId) &&
    notEmpty(mod.versionId) &&
    notEmpty(mod.downloadUrl)
  );
}

/**
 * Whether a CurseForge manifest can address this jar. Its files[] entry is two integers - a
 * CurseForge project id and a file id - and carries no filename, no URL, no hash and no size, so
 * only a mod imported from CurseForge with both ids recorded qualifies. Modrinth ids are base62
 * strings and can never read as integers, which is what stops a Modrinth mod taking this branch by
 * accident. Nothing records CurseForge provenance yet, so today this is always false and every jar
 * ships inline - which is a valid, importable pack, not a compromise.
 */
export function linksToCurseForge(mod: ModDto): boolean {
  return (
    mod.source === MOD_SOURCE.curseForge && isInteger(mod.projectId) && isInteger(mod.versionId)
  );
}

/** Counts the two piles for one format. */
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
      // A Prism instance is a materialised game directory rather than a manifest - there is nothing
      // in the format that can reference a file it does not contain - so every jar is bytes. An
      // unrecognised format falls here too: bundling everything is the honest worst case to quote.
      return false;
  }
}

function notEmpty(value: string | null | undefined): boolean {
  return typeof value === 'string' && value.trim() !== '';
}

function isInteger(value: string | null | undefined): boolean {
  return typeof value === 'string' && /^\d+$/.test(value);
}
