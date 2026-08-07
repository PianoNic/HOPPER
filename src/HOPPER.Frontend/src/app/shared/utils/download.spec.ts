import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { downloadBlob, fileNameFromDisposition } from './download';

const FALLBACK = 'server-export.mrpack';

describe('downloadBlob', () => {
  const OBJECT_URL = 'blob:http://localhost/8f2c';

  let revoke: ReturnType<typeof vi.fn<(url: string) => void>>;
  let clicked: Array<{ anchor: HTMLAnchorElement; inDocument: boolean }>;

  beforeEach(() => {
    vi.useFakeTimers();
    clicked = [];
    revoke = vi.fn<(url: string) => void>();
    URL.createObjectURL = vi.fn(() => OBJECT_URL);
    URL.revokeObjectURL = revoke;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      clicked.push({ anchor: this, inDocument: document.body.contains(this) });
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
    document.body.innerHTML = '';
  });

  it('puts the anchor in the document before dispatching the click', () => {
    downloadBlob(new Blob(['jar']), 'survival-hopper.jar');

    expect(clicked).toHaveLength(1);
    expect(clicked[0].inDocument).toBe(true);
  });

  it('carries the file name and the object URL onto the anchor', () => {
    downloadBlob(new Blob(['jar']), 'survival-hopper.jar');

    expect(clicked[0].anchor.download).toBe('survival-hopper.jar');
    expect(clicked[0].anchor.getAttribute('href')).toBe(OBJECT_URL);
  });

  it('takes the anchor back out once the click is dispatched', () => {
    downloadBlob(new Blob(['jar']), 'survival-hopper.jar');

    expect(document.body.querySelector('a')).toBeNull();
  });

  it('does not revoke the object URL in the click tick', () => {
    downloadBlob(new Blob(['jar']), 'survival-hopper.jar');

    expect(revoke).not.toHaveBeenCalled();
  });

  it('revokes the object URL once the grace period elapses', () => {
    downloadBlob(new Blob(['jar']), 'survival-hopper.jar');
    vi.advanceTimersByTime(60_000);

    expect(revoke).toHaveBeenCalledTimes(1);
    expect(revoke).toHaveBeenCalledWith(OBJECT_URL);
  });
});

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
