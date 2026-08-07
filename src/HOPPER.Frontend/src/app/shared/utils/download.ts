// A blob URL has to outlive the click. `click()` only queues the download; the browser resolves
// the URL after the current task ends, so revoking on the next line can detach it first and the
// download silently does nothing. There is no completion event to wait on and no error to catch -
// the caller has already reported success - so the URL is held for a minute instead. The anchor is
// in the document for the same reason: a detached one has had its `download` attribute ignored.
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
