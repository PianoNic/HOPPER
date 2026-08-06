// Saving a response body to disk and reading an error out of a binary response. Both exist only
// because GET /api/servers/{id}/jar is the one endpoint that answers with bytes rather than JSON,
// and the ordinary helpers in format.ts assume a parsed body on both paths.

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
