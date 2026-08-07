import { ImportStatus } from '../api/model/importStatus';
import { PackFormat } from '../api/model/packFormat';
import { PendingReason } from '../api/model/pendingReason';
import { PendingModDto } from '../api/model/pendingModDto';
import { toNumber } from '../shared/utils/format';

export function importStatusLabel(status: ImportStatus): string {
  switch (status) {
    case ImportStatus.Queued:
      return 'Queued';
    case ImportStatus.Running:
      return 'Running';
    case ImportStatus.Completed:
      return 'Completed';
    case ImportStatus.Failed:
      return 'Failed';
    default:
      return 'Unknown';
  }
}

export function isImportPending(status: ImportStatus): boolean {
  return status === ImportStatus.Queued || status === ImportStatus.Running;
}

export function packFormatLabel(format: PackFormat): string {
  switch (format) {
    case PackFormat.Modrinth:
      return 'Modrinth pack';
    case PackFormat.CurseForge:
      return 'CurseForge pack';
    case PackFormat.PrismInstance:
      return 'Prism instance';
    case PackFormat.JarArchive:
      return 'Zip of jars';
    default:
      return 'Not detected';
  }
}

export function pendingReasonLabel(reason: PendingReason): string {
  switch (reason) {
    case PendingReason.NoApiKey:
      return 'No CurseForge key';
    case PendingReason.Blocked:
      return 'Blocked by the author';
    case PendingReason.DownloadFailed:
      return 'Download failed';
    case PendingReason.HashMismatch:
      return 'Hash mismatch';
    default:
      return 'Pending';
  }
}

export function pendingReasonDetail(reason: PendingReason): string {
  switch (reason) {
    case PendingReason.NoApiKey:
      return 'A CurseForge pack names its mods by project and file id only - no filename, no URL, no hash. Without CurseForge:ApiKey configured, HOPPER cannot resolve them, so download this file yourself and upload it with the others.';
    case PendingReason.Blocked:
      return 'The author disabled third-party distribution, so the CurseForge API returns no download URL. This one always has to be fetched by hand, key or not.';
    case PendingReason.DownloadFailed:
      return 'Every mirror the pack listed failed, or its host is not on the allow-list. Fetch the jar yourself and upload it.';
    case PendingReason.HashMismatch:
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
