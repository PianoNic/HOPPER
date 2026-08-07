import { describe, expect, it } from 'vitest';
import { importStatusLabel, isImportPending, packFormatLabel, pendingLabel, pendingProjectUrl, pendingReasonDetail, pendingReasonLabel } from './import-labels';
import { ImportStatus } from '../api/model/importStatus';
import { PackFormat } from '../api/model/packFormat';
import { PendingReason } from '../api/model/pendingReason';
import { PendingModDto } from '../api/model/pendingModDto';

function pending(overrides: Partial<PendingModDto> = {}): PendingModDto {
  return {
    id: 'pending-id',
    importId: 'import-id',
    reason: PendingReason.NoApiKey,
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
    expect(importStatusLabel(ImportStatus.Queued)).toBe('Queued');
    expect(importStatusLabel(ImportStatus.Running)).toBe('Running');
    expect(importStatusLabel(ImportStatus.Completed)).toBe('Completed');
    expect(importStatusLabel(ImportStatus.Failed)).toBe('Failed');
  });

  it('does not pass an unknown status off as a known one', () => {
    expect(importStatusLabel(99)).toBe('Unknown');
  });
});

describe('isImportPending', () => {
  it('is true only while the worker still owns the row', () => {
    expect(isImportPending(ImportStatus.Queued)).toBe(true);
    expect(isImportPending(ImportStatus.Running)).toBe(true);
    expect(isImportPending(ImportStatus.Completed)).toBe(false);
    expect(isImportPending(ImportStatus.Failed)).toBe(false);
  });

  it('stops polling on a status this build does not know', () => {
    expect(isImportPending(42)).toBe(false);
  });
});

describe('packFormatLabel', () => {
  it('names the formats the detector produces', () => {
    expect(packFormatLabel(PackFormat.Modrinth)).toBe('Modrinth pack');
    expect(packFormatLabel(PackFormat.CurseForge)).toBe('CurseForge pack');
    expect(packFormatLabel(PackFormat.PrismInstance)).toBe('Prism instance');
    expect(packFormatLabel(PackFormat.JarArchive)).toBe('Zip of jars');
    expect(packFormatLabel(PackFormat.Unknown)).toBe('Not detected');
  });
});

describe('pendingReasonLabel / pendingReasonDetail', () => {
  it('separates the keyless case from the genuinely blocked one', () => {
    expect(pendingReasonLabel(PendingReason.NoApiKey)).toBe('No CurseForge key');
    expect(pendingReasonLabel(PendingReason.Blocked)).toBe('Blocked by the author');
    expect(pendingReasonDetail(PendingReason.NoApiKey)).toContain('CurseForge:ApiKey');
    expect(pendingReasonDetail(PendingReason.Blocked)).toContain('third-party distribution');
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
