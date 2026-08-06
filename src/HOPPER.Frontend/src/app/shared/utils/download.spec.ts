import { describe, expect, it } from 'vitest';
import { fileNameFromDisposition } from './download';

const FALLBACK = 'server-export.mrpack';

describe('fileNameFromDisposition', () => {
  it('reads the plain filename ASP.NET sends', () => {
    expect(
      fileNameFromDisposition('attachment; filename=survival-20260806-143000.mrpack', FALLBACK),
    ).toBe('survival-20260806-143000.mrpack');
  });

  it('unquotes a quoted filename', () => {
    expect(fileNameFromDisposition('attachment; filename="my pack.zip"', FALLBACK)).toBe(
      'my pack.zip',
    );
  });

  it('prefers the RFC 6266 encoded name and decodes it', () => {
    expect(
      fileNameFromDisposition(
        `attachment; filename=pack.zip; filename*=UTF-8''h%C3%BCtte-20260806.zip`,
        FALLBACK,
      ),
    ).toBe('hütte-20260806.zip');
  });

  it('falls back when the header is absent or unparseable', () => {
    expect(fileNameFromDisposition(null, FALLBACK)).toBe(FALLBACK);
    expect(fileNameFromDisposition('attachment', FALLBACK)).toBe(FALLBACK);
  });

  it('keeps only the last segment of anything path-shaped', () => {
    expect(fileNameFromDisposition('attachment; filename="../../etc/passwd"', FALLBACK)).toBe(
      'passwd',
    );
    expect(fileNameFromDisposition('attachment; filename="C:\\temp\\pack.zip"', FALLBACK)).toBe(
      'pack.zip',
    );
    expect(fileNameFromDisposition('attachment; filename=".."', FALLBACK)).toBe(FALLBACK);
  });
});
