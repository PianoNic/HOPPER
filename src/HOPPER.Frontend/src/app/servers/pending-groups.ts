import { ModImportDto } from '../api/model/modImportDto';
import { PendingModDto } from '../api/model/pendingModDto';
import { PackFormat } from '../api/model/packFormat';

export interface PendingGroup {
  readonly importId: string;

  readonly sourceName: string;
  readonly format: PackFormat;

  readonly createdAt: string | null;
  readonly entries: ReadonlyArray<PendingModDto>;
}

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
      format: row?.format ?? PackFormat.Unknown,
      createdAt: row?.createdAt ?? null,
      entries: rows,
    });
  }

  return groups.sort(newestFirst);
}

function newestFirst(a: PendingGroup, b: PendingGroup): number {
  const left = a.createdAt === null ? Number.NEGATIVE_INFINITY : Date.parse(a.createdAt);
  const right = b.createdAt === null ? Number.NEGATIVE_INFINITY : Date.parse(b.createdAt);
  return right - left;
}
