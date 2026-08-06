import { Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs/operators';

/**
 * The `:id` of the server the current route is about, as a signal.
 *
 * Every per-server page needs this, and it has to be a signal rather than a snapshot read: the
 * router reuses a component when only a path parameter changes, so navigating from one server's
 * mods page to another's would otherwise leave the second server showing the first one's jars.
 * The snapshot seeds the initial value so the first render already has the id and no page flashes
 * an empty state before its first load.
 *
 * Deliberately a plain function rather than an injectable: it is a route-reading convenience, not
 * a service, and calling it in a field initialiser keeps it inside the component's injection
 * context without any provider wiring.
 */
export function serverIdSignal(route: ActivatedRoute): Signal<string> {
  return toSignal(
    route.paramMap.pipe(map((params) => params.get('id') ?? '')),
    { initialValue: route.snapshot.paramMap.get('id') ?? '' },
  );
}
