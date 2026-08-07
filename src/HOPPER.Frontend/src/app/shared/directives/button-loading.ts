import { booleanAttribute, Directive, input } from '@angular/core';

/**
 * `<button hlmBtn [loading]="saving()">Save</button>`.
 *
 * Spartan has no loading state, in helm or in brain, so this adds one. A directive cannot put an
 * element inside its host, so the spinner is a `::before` drawn from `data-loading` in styles.css.
 * That keeps it out of every template and works on buttons nobody has touched yet: hlmBtn is
 * already `inline-flex` with a gap, so the pseudo-element lands beside the label on its own.
 */
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
