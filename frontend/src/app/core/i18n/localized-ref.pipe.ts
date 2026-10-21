import { Pipe, PipeTransform } from '@angular/core';

import { activeLanguageCode } from './translate';

/**
 * Gives a constant list (`localizedList`) a new reference when the language changes, so PrimeNG
 * inputs such as `[options]` and `[model]` re-read their translated labels.
 */
@Pipe({ name: 'localizedRef', pure: false })
export class LocalizedRefPipe implements PipeTransform {
  private source: unknown;
  private lang = '';
  private copy: unknown;

  transform<T>(value: T): T {
    if (!Array.isArray(value)) {
      return value;
    }
    const lang = activeLanguageCode();
    if (this.source !== value || this.lang !== lang) {
      this.source = value;
      this.lang = lang;
      this.copy = value.slice();
    }
    return this.copy as T;
  }
}
