import { Pipe, PipeTransform } from '@angular/core';

/**
 * A timestamp in the reader's own locale and time zone.
 *
 * `Intl` rather than Angular's `DatePipe`: the pipe formats against `LOCALE_ID`, which is `en-US`
 * unless something provides otherwise, and any other locale needs `registerLocaleData` or it throws
 * at runtime. `Intl` follows the browser with no registration and no locale data to ship.
 */
@Pipe({ name: 'when' })
export class WhenPipe implements PipeTransform {
  transform(value: string | Date | null | undefined, style: 'short' | 'long' | 'date' = 'short'): string {
    if (value == null || value === '') return '';

    const at = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(at.getTime())) return '';

    return FORMATS[style].format(at);
  }
}

// Built once. Constructing an Intl.DateTimeFormat is the expensive part, and a table cell renders
// it per row.
const FORMATS: Record<'short' | 'long' | 'date', Intl.DateTimeFormat> = {
  short: new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }),
  long: new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }),
  date: new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }),
};
