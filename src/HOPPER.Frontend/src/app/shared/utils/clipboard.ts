export type CopyOutcome = 'copied' | 'failed';

export async function copyText(value: string): Promise<CopyOutcome> {
  if (window.isSecureContext && navigator.clipboard) {
    try {
      await navigator.clipboard.writeText(value);
      return 'copied';
    } catch {
    }
  }

  return legacyCopy(value) ? 'copied' : 'failed';
}

function legacyCopy(value: string): boolean {
  const area = document.createElement('textarea');
  area.value = value;

  area.readOnly = true;
  area.setAttribute('aria-hidden', 'true');
  area.style.position = 'fixed';
  area.style.top = '0';
  area.style.left = '0';
  area.style.opacity = '0';
  document.body.appendChild(area);

  try {
    area.select();
    area.setSelectionRange(0, value.length);
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    area.remove();
  }
}
