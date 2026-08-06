// Saving a response body to disk, naming it, and reading an error out of a binary response. All
// three exist because two endpoints answer with bytes rather than JSON - GET
// /api/servers/{id}/jar and GET /api/servers/{id}/export - and the ordinary helpers in format.ts
// assume a parsed body on both paths.

/**
 * Hands a Blob to the browser's downloader under a chosen filename. An <a download> click is the
 * only way to name a file the app fetched itself: the Content-Disposition the server sent is
 * attached to an XHR response, not to a navigation, so the browser never sees it.
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

/**
 * Reads the filename out of a Content-Disposition header, falling back when there is none.
 *
 * An exported pack is named by the server - slug plus the UTC minute it was written - and that name
 * is the only thing telling two exports of the same server apart. Rebuilding it on the dashboard
 * would mean guessing the server's clock, so the header is read instead. It is visible to
 * cross-origin JavaScript only because the API names it in WithExposedHeaders; the fallback covers
 * a deployment that strips it at a proxy.
 *
 * Both spellings are handled: RFC 6266 puts the percent-encoded UTF-8 name in `filename*` and the
 * plain one in `filename`, and ASP.NET sends both. Any path separator in the result is dropped -
 * this value ends up in a download attribute, and a header is not something to trust with a path.
 */
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

/**
 * The jar endpoint is requested with responseType 'blob', so a failure arrives with the `{ "error":
 * "..." }` body as an unparsed Blob and messageFrom() sees nothing but an HttpErrorResponse. That
 * matters here more than anywhere else: the 503 raised when Hopper:LocatorTemplatePath is unset
 * names the configuration key to set, and swallowing it leaves an admin staring at a generic
 * failure with no idea the deployment simply never built the template.
 */
export async function messageFromBlobError(err: unknown, fallback: string): Promise<string> {
  const body = (err as { error?: unknown } | null)?.error;
  if (!(body instanceof Blob)) return fallback;

  try {
    const parsed = JSON.parse(await body.text()) as { error?: string; detail?: string };
    return parsed.error ?? parsed.detail ?? fallback;
  } catch {
    // A proxy or the dev server can answer with HTML rather than HOPPER's own error shape.
    return fallback;
  }
}
