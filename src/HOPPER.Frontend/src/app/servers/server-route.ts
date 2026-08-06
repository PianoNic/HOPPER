import { Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs/operators';

export function serverIdSignal(route: ActivatedRoute): Signal<string> {
  return toSignal(
    route.paramMap.pipe(map((params) => params.get('id') ?? '')),
    { initialValue: route.snapshot.paramMap.get('id') ?? '' },
  );
}
