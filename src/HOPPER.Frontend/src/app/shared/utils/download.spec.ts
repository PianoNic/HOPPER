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

  // ASP.NET sends both spellings whenever the name is not plain ASCII, and the percent-encoded one
  // is the accurate half - the plain one is a transliteration.
  it('prefers the RFC 6266 encoded name and decodes it', () => {
    expect(
      fileNameFromDisposition(
        `attachment; filename=pack.zip; filename*=UTF-8''h%C3%BCtte-20260806.zip`,
        FALLBACK,
      ),
    ).toBe('hütte-20260806.zip');
  });

  // A proxy that drops the header, or a cross-origin deployment that forgot to expose it, must not
  // land the file on disk under a blank name.
  it('falls back when the header is absent or unparseable', () => {
    expect(fileNameFromDisposition(null, FALLBACK)).toBe(FALLBACK);
    expect(fileNameFromDisposition('attachment', FALLBACK)).toBe(FALLBACK);
  });

  // The header is a server-supplied string reaching a download attribute. It is not a path, and
  // anything shaped like one is reduced to its last segment before it gets there.
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
