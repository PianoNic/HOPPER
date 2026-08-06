import { describe, expect, it } from 'vitest';
import {
  IMPORT_STATUS,
  PACK_FORMAT,
  PENDING_REASON,
  importStatusLabel,
  isImportPending,
  packFormatLabel,
  pendingLabel,
  pendingProjectUrl,
  pendingReasonDetail,
  pendingReasonLabel,
} from './import-labels';
import { PendingModDto } from '../api/model/pendingModDto';

function pending(overrides: Partial<PendingModDto> = {}): PendingModDto {
  return {
    id: 'pending-id',
    importId: 'import-id',
    reason: PENDING_REASON.noApiKey,
    displayName: null,
    fileName: null,
    projectId: null,
    fileId: null,
    expectedSha1: null,
    sourceUrl: null,
    detail: null,
    createdAt: '2026-08-06T10:00:00Z',
    ...overrides,
  };
}

describe('importStatusLabel', () => {
  it('names every status HOPPER can persist', () => {
    expect(importStatusLabel(IMPORT_STATUS.queued)).toBe('Queued');
    expect(importStatusLabel(IMPORT_STATUS.running)).toBe('Running');
    expect(importStatusLabel(IMPORT_STATUS.completed)).toBe('Completed');
    expect(importStatusLabel(IMPORT_STATUS.failed)).toBe('Failed');
  });

  it('does not pass an unknown status off as a known one', () => {
    expect(importStatusLabel(99)).toBe('Unknown');
  });
});

describe('isImportPending', () => {
  it('is true only while the worker still owns the row', () => {
    expect(isImportPending(IMPORT_STATUS.queued)).toBe(true);
    expect(isImportPending(IMPORT_STATUS.running)).toBe(true);
    expect(isImportPending(IMPORT_STATUS.completed)).toBe(false);
    expect(isImportPending(IMPORT_STATUS.failed)).toBe(false);
  });

  it('stops polling on a status this build does not know', () => {
    expect(isImportPending(42)).toBe(false);
  });
});

describe('packFormatLabel', () => {
  it('names the formats the detector produces', () => {
    expect(packFormatLabel(PACK_FORMAT.modrinth)).toBe('Modrinth pack');
    expect(packFormatLabel(PACK_FORMAT.curseForge)).toBe('CurseForge pack');
    expect(packFormatLabel(PACK_FORMAT.prismInstance)).toBe('Prism instance');
    expect(packFormatLabel(PACK_FORMAT.jarArchive)).toBe('Zip of jars');
    expect(packFormatLabel(PACK_FORMAT.unknown)).toBe('Not detected');
  });
});

describe('pendingReasonLabel / pendingReasonDetail', () => {
  it('separates the keyless case from the genuinely blocked one', () => {
    expect(pendingReasonLabel(PENDING_REASON.noApiKey)).toBe('No CurseForge key');
    expect(pendingReasonLabel(PENDING_REASON.blocked)).toBe('Blocked by the author');
    expect(pendingReasonDetail(PENDING_REASON.noApiKey)).toContain('CurseForge:ApiKey');
    expect(pendingReasonDetail(PENDING_REASON.blocked)).toContain('third-party distribution');
  });

  it('has wording for every reason, including one it does not recognise', () => {
    for (const reason of [0, 1, 2, 3, 77]) {
      expect(pendingReasonLabel(reason).length).toBeGreaterThan(0);
      expect(pendingReasonDetail(reason).length).toBeGreaterThan(0);
    }
  });
});

describe('pendingProjectUrl', () => {
  it('prefers the URL the importer recorded', () => {
    expect(pendingProjectUrl(pending({ sourceUrl: 'https://cdn.modrinth.com/x.jar' }))).toBe(
      'https://cdn.modrinth.com/x.jar',
    );
  });

  it('synthesizes the redirecting project link when only the ids are known', () => {
    expect(pendingProjectUrl(pending({ projectId: 351491, fileId: 6366217 }))).toBe(
      'https://www.curseforge.com/projects/351491',
    );
  });

  it('links nowhere when there is nothing to link to', () => {
    expect(pendingProjectUrl(pending())).toBeNull();
  });
});

describe('pendingLabel', () => {
  it('uses the friendliest name available', () => {
    expect(pendingLabel(pending({ displayName: 'Create', fileName: 'create.jar' }))).toBe('Create');
    expect(pendingLabel(pending({ fileName: 'create.jar' }))).toBe('create.jar');
  });

  it('falls back to the two integers, which is all a keyless CurseForge pack carries', () => {
    expect(pendingLabel(pending({ projectId: 351491, fileId: 6366217 }))).toBe(
      'Project 351491, file 6366217',
    );
    expect(pendingLabel(pending({ projectId: 351491 }))).toBe('Project 351491');
    expect(pendingLabel(pending())).toBe('Unidentified file');
  });
});
