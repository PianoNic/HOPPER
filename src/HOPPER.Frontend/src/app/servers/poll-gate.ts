export type PollState = {
  hidden: boolean;
  hasServer: boolean;
  failed: boolean;
};

// `failed` is what keeps a background poll from becoming a toast storm: the same flag that
// suppressed the second toast also stops the requests, so there is one rule rather than two
// mechanisms that have to agree. `hidden` is a separate concern - browsers throttle a background
// interval to roughly once a minute rather than stopping it, so this is about not burning requests
// on a dashboard left open on a second monitor.
export function shouldPoll(state: PollState): boolean {
  return !state.hidden && state.hasServer && !state.failed;
}
