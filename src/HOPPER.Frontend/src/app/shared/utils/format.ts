export function toNumber(value: unknown): number {
  const n = Number(value as number);
  return Number.isFinite(n) ? n : 0;
}

export function apiNumber<T>(value: number): T {
  return value as unknown as T;
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

export function messageFrom(err: unknown, fallback: string): string {
  const body = (err as { error?: { error?: string; detail?: string } } | null)?.error;
  if (body?.error) return body.error;
  if (body?.detail) return body.detail;
  return err instanceof Error ? err.message : fallback;
}
