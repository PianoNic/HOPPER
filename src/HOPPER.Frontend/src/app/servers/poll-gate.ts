export type PollState = {
  hidden: boolean;
  hasServer: boolean;
  failed: boolean;
};

export function shouldPoll(state: PollState): boolean {
  return !state.hidden && state.hasServer && !state.failed;
}
