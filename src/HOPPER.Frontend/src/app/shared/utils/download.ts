import { toast } from '@spartan-ng/brain/sonner';
import type { ServersService } from '../../api/api/servers.service';
import type { ServerDto } from '../../api/model/serverDto';

const REVOKE_AFTER_MS = 60_000;

export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), REVOKE_AFTER_MS);
}

export function fileNameFromDisposition(header: string | null, fallback: string): string {
  if (!header) return fallback;

  const extended = /filename\*=UTF-8''([^;]+)/i.exec(header);
  const plain = /filename="?([^";]+)"?/i.exec(header);

  let name = fallback;
  if (extended) {
    try {
      name = decodeURIComponent(extended[1]);
    } catch {
      name = plain ? plain[1] : fallback;
    }
  } else if (plain) {
    name = plain[1];
  }

  name = name.trim().split(/[\\/]/).pop() ?? '';
  return name === '' || name === '.' || name === '..' ? fallback : name;
}

export function downloadServerJar(
  api: ServersService,
  server: Pick<ServerDto, 'id' | 'slug'>,
  busy: (running: boolean) => void,
  failure = 'Failed to build the jar',
): void {
  busy(true);

  api.apiServersIdJarGet(server.id).subscribe({
    next: (jar) => {
      downloadBlob(jar as unknown as Blob, `${server.slug}-hopper.jar`);
      busy(false);
    },
    error: async (err) => {
      toast.error(await messageFromBlobError(err, failure));
      busy(false);
    },
  });
}

export async function messageFromBlobError(err: unknown, fallback: string): Promise<string> {
  const body = (err as { error?: unknown } | null)?.error;
  if (!(body instanceof Blob)) return fallback;

  try {
    const parsed = JSON.parse(await body.text()) as { error?: string; detail?: string };
    return parsed.error ?? parsed.detail ?? fallback;
  } catch {
    return fallback;
  }
}
