import { PendingModDto } from '../api/model/pendingModDto';
import { toNumber } from '../shared/utils/format';

export const IMPORT_STATUS = {
  queued: 0,
  running: 1,
  completed: 2,
  failed: 3,
} as const;

export const PACK_FORMAT = {
  unknown: 0,
  modrinth: 1,
  curseForge: 2,
  prismInstance: 3,
  jarArchive: 4,
} as const;

export const PENDING_REASON = {
  noApiKey: 0,
  blocked: 1,
  downloadFailed: 2,
  hashMismatch: 3,
} as const;

export function importStatusLabel(status: number): string {
  switch (status) {
    case IMPORT_STATUS.queued:
      return 'Queued';
    case IMPORT_STATUS.running:
      return 'Running';
    case IMPORT_STATUS.completed:
      return 'Completed';
    case IMPORT_STATUS.failed:
      return 'Failed';
    default:
      return 'Unknown';
  }
}

export function isImportPending(status: number): boolean {
  return status === IMPORT_STATUS.queued || status === IMPORT_STATUS.running;
}

export function packFormatLabel(format: number): string {
  switch (format) {
    case PACK_FORMAT.modrinth:
      return 'Modrinth pack';
    case PACK_FORMAT.curseForge:
      return 'CurseForge pack';
    case PACK_FORMAT.prismInstance:
      return 'Prism instance';
    case PACK_FORMAT.jarArchive:
      return 'Zip of jars';
    default:
      return 'Not detected';
  }
}

export function pendingReasonLabel(reason: number): string {
  switch (reason) {
    case PENDING_REASON.noApiKey:
      return 'No CurseForge key';
    case PENDING_REASON.blocked:
      return 'Blocked by the author';
    case PENDING_REASON.downloadFailed:
      return 'Download failed';
    case PENDING_REASON.hashMismatch:
      return 'Hash mismatch';
    default:
      return 'Pending';
  }
}

export function pendingReasonDetail(reason: number): string {
  switch (reason) {
    case PENDING_REASON.noApiKey:
      return 'A CurseForge pack names its mods by project and file id only - no filename, no URL, no hash. Without CurseForge:ApiKey configured, HOPPER cannot resolve them, so download this file yourself and upload it with the others.';
    case PENDING_REASON.blocked:
      return 'The author disabled third-party distribution, so the CurseForge API returns no download URL. This one always has to be fetched by hand, key or not.';
    case PENDING_REASON.downloadFailed:
      return 'Every mirror the pack listed failed, or its host is not on the allow-list. Fetch the jar yourself and upload it.';
    case PENDING_REASON.hashMismatch:
      return 'The bytes that arrived are not what the pack index described, so they were discarded rather than stored under a wrong name.';
    default:
      return 'HOPPER could not store this file automatically.';
  }
}

export function pendingProjectUrl(pending: PendingModDto): string | null {
  if (pending.sourceUrl && pending.sourceUrl.length > 0) return pending.sourceUrl;

  const projectId = toNumber(pending.projectId);
  return projectId > 0 ? `https://www.curseforge.com/projects/${projectId}` : null;
}

export function pendingLabel(pending: PendingModDto): string {
  if (pending.displayName && pending.displayName.length > 0) return pending.displayName;
  if (pending.fileName && pending.fileName.length > 0) return pending.fileName;

  const projectId = toNumber(pending.projectId);
  const fileId = toNumber(pending.fileId);
  if (projectId > 0) {
    return fileId > 0 ? `Project ${projectId}, file ${fileId}` : `Project ${projectId}`;
  }

  return 'Unidentified file';
}
