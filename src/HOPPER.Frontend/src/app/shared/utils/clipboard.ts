export type CopyOutcome = 'copied' | 'failed';

// The single clipboard path in the dashboard. HOPPER is usually reached over plain http on a LAN,
// where the async clipboard API is either absent (Chrome drops it outside a secure context) or
// present and rejecting (Firefox). The legacy path is not gated on that permission, so trying it
// turns the common case back into a working button instead of a toast the user cannot act on.
export async function copyText(value: string): Promise<CopyOutcome> {
  if (window.isSecureContext && navigator.clipboard) {
    try {
      await navigator.clipboard.writeText(value);
      return 'copied';
    } catch {
      // Denied by permission policy, which the fallback below does not need.
    }
  }

  return legacyCopy(value) ? 'copied' : 'failed';
}

function legacyCopy(value: string): boolean {
  const area = document.createElement('textarea');
  area.value = value;
  // readOnly and a zero-opacity fixed box keep this off screen without scrolling the page or
  // opening a keyboard on mobile between the append and the removal.
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
