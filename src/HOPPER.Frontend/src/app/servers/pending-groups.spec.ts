import { describe, expect, it } from 'vitest';
import { groupPendingByImport } from './pending-groups';
import { IMPORT_STATUS, PACK_FORMAT, PENDING_REASON } from './import-labels';
import { ModImportDto } from '../api/model/modImportDto';
import { PendingModDto } from '../api/model/pendingModDto';

function pending(id: string, importId: string): PendingModDto {
  return {
    id,
    importId,
    reason: PENDING_REASON.noApiKey,
    displayName: null,
    fileName: null,
    projectId: null,
    fileId: null,
    expectedSha1: null,
    sourceUrl: null,
    detail: null,
    createdAt: '2026-08-06T10:00:00Z',
  };
}

function imported(id: string, sourceName: string, createdAt: string): ModImportDto {
  return {
    id,
    sourceName,
    sourceKind: 0,
    format: PACK_FORMAT.curseForge,
    status: IMPORT_STATUS.completed,
    importedCount: 0,
    skippedCount: 0,
    pendingCount: 0,
    failedCount: 0,
    error: null,
    startedAt: createdAt,
    completedAt: createdAt,
    createdBy: null,
    createdAt,
  };
}

describe('groupPendingByImport', () => {
  it('groups entries under the import that produced them, newest import first', () => {
    const groups = groupPendingByImport(
      [pending('a', 'old'), pending('b', 'new'), pending('c', 'old')],
      [
        imported('old', 'AllTheMods-9.zip', '2026-08-01T09:00:00Z'),
        imported('new', 'Cottage-Witch.mrpack', '2026-08-05T09:00:00Z'),
      ],
    );

    expect(groups.map((g) => g.sourceName)).toEqual(['Cottage-Witch.mrpack', 'AllTheMods-9.zip']);
    expect(groups[0].entries.map((e) => e.id)).toEqual(['b']);

    expect(groups[1].entries.map((e) => e.id)).toEqual(['a', 'c']);
  });

  it('carries the import format and date onto the group', () => {
    const groups = groupPendingByImport(
      [pending('a', 'one')],
      [imported('one', 'pack.zip', '2026-08-01T09:00:00Z')],
    );

    expect(groups[0].format).toBe(PACK_FORMAT.curseForge);
    expect(groups[0].createdAt).toBe('2026-08-01T09:00:00Z');
  });

  it('still lists entries whose import is not in the list, and sorts them last', () => {
    const groups = groupPendingByImport(
      [pending('orphan', 'gone'), pending('a', 'known')],
      [imported('known', 'pack.zip', '2026-08-01T09:00:00Z')],
    );

    expect(groups).toHaveLength(2);
    expect(groups[0].sourceName).toBe('pack.zip');
    expect(groups[1].sourceName).toBe('Import no longer listed');
    expect(groups[1].format).toBe(PACK_FORMAT.unknown);
    expect(groups[1].createdAt).toBeNull();
    expect(groups[1].entries.map((e) => e.id)).toEqual(['orphan']);
  });

  it('answers with nothing when there is nothing open', () => {
    expect(groupPendingByImport([], [imported('one', 'pack.zip', '2026-08-01T09:00:00Z')])).toEqual(
      [],
    );
  });
});
