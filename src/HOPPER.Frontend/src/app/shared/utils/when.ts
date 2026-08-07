import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'when' })
export class WhenPipe implements PipeTransform {
  transform(value: string | Date | null | undefined, style: 'short' | 'long' | 'date' = 'short'): string {
    if (value == null || value === '') return '';

    const at = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(at.getTime())) return '';

    return FORMATS[style].format(at);
  }
}

const FORMATS: Record<'short' | 'long' | 'date', Intl.DateTimeFormat> = {
  short: new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }),
  long: new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }),
  date: new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }),
};
