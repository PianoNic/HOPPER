import { describe, expect, it } from 'vitest';
import { shouldPoll } from './poll-gate';

const HEALTHY = { hidden: false, hasServer: true, failed: false };

describe('shouldPoll', () => {
  it('polls when the tab is visible, a server is selected and the last poll succeeded', () => {
    expect(shouldPoll(HEALTHY)).toBe(true);
  });

  it('does not poll while the tab is hidden', () => {
    expect(shouldPoll({ ...HEALTHY, hidden: true })).toBe(false);
  });

  it('does not poll before a server id is known', () => {
    expect(shouldPoll({ ...HEALTHY, hasServer: false })).toBe(false);
  });

  it('does not poll again once a poll has failed', () => {
    expect(shouldPoll({ ...HEALTHY, failed: true })).toBe(false);
  });
});
