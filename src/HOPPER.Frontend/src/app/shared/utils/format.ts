// Small pure formatters shared by the mods, clients and overview pages. Deliberately plain
// functions rather than pipes: templates call them through a component method, which keeps the
// no-arrow-functions-in-templates rule intact and avoids three near-identical pipe classes.

/**
 * The .NET `long` on ModDto.size comes through the OpenAPI generator as an opaque
 * `ManifestModDtoSize` interface (the generator cannot narrow .NET's integer|string schema), so
 * every caller has to coerce. Doing it here means exactly one `as unknown` in the app.
 */
export function toNumber(value: unknown): number {
  const n = Number(value as number);
  return Number.isFinite(n) ? n : 0;
}

const UNITS = ['B', 'KB', 'MB', 'GB'] as const;

export function formatBytes(bytes: number): string {
  if (bytes <= 0) return '0 B';
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${unit === 0 ? value : value.toFixed(1)} ${UNITS[unit]}`;
}

/**
 * "3m ago" style age. `now` is passed in rather than read from Date.now() so the caller owns the
 * clock: the pages tick a signal on their poll interval, which is what makes these labels refresh
 * under OnPush instead of freezing at first render.
 */
export function formatAge(iso: string | undefined, now: number): string {
  if (!iso) return 'never';
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return 'never';

  const seconds = Math.max(0, Math.round((now - then) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

/** Reads HOPPER's `{ "error": "..." }` bodies, falling back to ProblemDetails and then the HTTP error. */
export function messageFrom(err: unknown, fallback: string): string {
  const body = (err as { error?: { error?: string; detail?: string } } | null)?.error;
  if (body?.error) return body.error;
  if (body?.detail) return body.detail;
  return err instanceof Error ? err.message : fallback;
}
