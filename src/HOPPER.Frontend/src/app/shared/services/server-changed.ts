import { Injectable, signal } from '@angular/core';

/**
 * Bumped by whatever changes a server's mods or clients, watched by whatever shows a count of them.
 *
 * The sidebar outlives the pages that change those numbers and has no other way to hear about it:
 * a mod added on the Mods page does not change the route, so nothing the sidebar already watches
 * moves. A revision rather than the counts themselves, so the sidebar keeps one source of truth
 * (the server it fetches) instead of two that can disagree.
 */
@Injectable({ providedIn: 'root' })
export class ServerChanged {
  readonly revision = signal(0);

  changed(): void {
    this.revision.update((n) => n + 1);
  }
}
