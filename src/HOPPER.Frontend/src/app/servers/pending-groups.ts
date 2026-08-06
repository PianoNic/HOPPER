// Turns the flat per-server pending list into the shape the checklist page renders.
//
// GET /api/servers/{id}/pending answers with every open entry on the server, from every import
// that ever produced one, in one array. That is the right wire shape - the rows are the server's
// open work, not one dialog's - but on screen a bare list of forty entries says nothing about
// which pack each one came from, and the entry itself carries only an importId.
//
// Kept free of Angular so it can be tested as what it is: a pure regrouping.

import { ModImportDto } from '../api/model/modImportDto';
import { PendingModDto } from '../api/model/pendingModDto';
import { PACK_FORMAT } from './import-labels';

export interface PendingGroup {
  readonly importId: string;
  /** The pack the entries came from, or a stand-in when its import row is no longer listed. */
  readonly sourceName: string;
  readonly format: number;
  /** When the import ran. Null when the import row could not be matched, which sorts it last. */
  readonly createdAt: string | null;
  readonly entries: ReadonlyArray<PendingModDto>;
}

/**
 * Groups pending entries by the import that produced them, newest import first.
 *
 * Entries keep the order the server sent them in - it is already the insertion order of the pack
 * index, so a group reads in the same order as the pack's own file list.
 *
 * An entry whose import is not in `imports` is still grouped and still rendered: the jar is open
 * work whether or not the dashboard can name its pack, and dropping it here would hide a row that
 * only a human can clear.
 */
export function groupPendingByImport(
  entries: ReadonlyArray<PendingModDto>,
  imports: ReadonlyArray<ModImportDto>,
): ReadonlyArray<PendingGroup> {
  const byId = new Map(imports.map((row) => [row.id, row]));
  const grouped = new Map<string, PendingModDto[]>();

  for (const entry of entries) {
    const existing = grouped.get(entry.importId);
    if (existing) existing.push(entry);
    else grouped.set(entry.importId, [entry]);
  }

  const groups: PendingGroup[] = [];
  for (const [importId, rows] of grouped) {
    const row = byId.get(importId);
    groups.push({
      importId,
      sourceName: row?.sourceName ?? 'Import no longer listed',
      format: row?.format ?? PACK_FORMAT.unknown,
      createdAt: row?.createdAt ?? null,
      entries: rows,
    });
  }

  return groups.sort(newestFirst);
}

/** Descending by import date. An unmatched import has no date and goes to the end. */
function newestFirst(a: PendingGroup, b: PendingGroup): number {
  const left = a.createdAt === null ? Number.NEGATIVE_INFINITY : Date.parse(a.createdAt);
  const right = b.createdAt === null ? Number.NEGATIVE_INFINITY : Date.parse(b.createdAt);
  return right - left;
}
