import { booleanAttribute, Directive, input } from '@angular/core';

@Directive({
  selector: 'button[hlmBtn][loading], a[hlmBtn][loading]',
  host: {
    '[attr.data-loading]': 'loading() ? "true" : null',
    '[attr.aria-busy]': 'loading() ? "true" : null',
  },
})
export class ButtonLoading {
  readonly loading = input(false, { transform: booleanAttribute });
}
