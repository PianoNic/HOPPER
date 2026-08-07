import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { copyText } from './clipboard';

const VALUE = 'level-name=world\nhopper.token=6b1f';

function secureContext(value: boolean): void {
  Object.defineProperty(window, 'isSecureContext', { value, configurable: true });
}

function clipboardApi(value: unknown): void {
  Object.defineProperty(navigator, 'clipboard', { value, configurable: true });
}

describe('copyText', () => {
  let execCommand: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    execCommand = vi.fn(() => true);
    document.execCommand = execCommand as unknown as typeof document.execCommand;
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('uses the async clipboard API in a secure context', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    secureContext(true);
    clipboardApi({ writeText });

    await expect(copyText(VALUE)).resolves.toBe('copied');
    expect(writeText).toHaveBeenCalledWith(VALUE);
    expect(execCommand).not.toHaveBeenCalled();
  });

  it('falls back to execCommand when navigator.clipboard is absent, as it is on plain http', async () => {
    secureContext(false);
    clipboardApi(undefined);

    await expect(copyText(VALUE)).resolves.toBe('copied');
    expect(execCommand).toHaveBeenCalledWith('copy');
  });

  it('falls back to execCommand when writeText rejects because permission was denied', async () => {
    secureContext(true);
    clipboardApi({ writeText: vi.fn(() => Promise.reject(new Error('Write permission denied.'))) });

    await expect(copyText(VALUE)).resolves.toBe('copied');
    expect(execCommand).toHaveBeenCalledWith('copy');
  });

  it('reports failed only when both paths fail', async () => {
    secureContext(true);
    clipboardApi({ writeText: vi.fn(() => Promise.reject(new Error('Write permission denied.'))) });
    execCommand.mockReturnValue(false);

    await expect(copyText(VALUE)).resolves.toBe('failed');
  });

  it('removes the scratch textarea whether the copy succeeded or failed', async () => {
    secureContext(false);
    clipboardApi(undefined);

    await copyText(VALUE);
    expect(document.body.querySelector('textarea')).toBeNull();

    execCommand.mockImplementation(() => {
      throw new Error('execCommand is not supported');
    });

    await expect(copyText(VALUE)).resolves.toBe('failed');
    expect(document.body.querySelector('textarea')).toBeNull();
  });
});
