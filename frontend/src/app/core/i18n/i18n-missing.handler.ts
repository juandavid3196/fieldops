import { Injectable, Injector, inject, isDevMode } from '@angular/core';
import {
  TranslocoMissingHandler,
  TranslocoMissingHandlerData,
  TranslocoService,
} from '@jsverse/transloco';

import { BASE_LANGUAGE } from './languages.config';

let misses = 0;

/** Number of missing-key lookups so far; a lazily built constant is not cached when it grows. */
export function missingKeyCount(): number {
  return misses;
}

/**
 * BR-11: development renders the key and logs one warning per language and key; production renders
 * the English value silently (parity checked by `npm run i18n:check`, so this only covers a scope
 * that failed to load).
 */
@Injectable()
export class FieldOpsMissingHandler implements TranslocoMissingHandler {
  private readonly injector = inject(Injector);
  private readonly warned = new Set<string>();

  handle(key: string, data: TranslocoMissingHandlerData, params?: Record<string, unknown>): string {
    misses++;
    const lang = data.activeLang;
    if (isDevMode()) {
      const id = `${lang}:${key}`;
      if (!this.warned.has(id)) {
        this.warned.add(id);
        console.warn(`[i18n] Missing translation "${key}" for "${lang}".`);
      }
      return key;
    }

    if (lang !== BASE_LANGUAGE) {
      const transloco = this.injector.get(TranslocoService);
      const english = transloco.getTranslation(BASE_LANGUAGE)[key];
      if (typeof english === 'string' && english !== '') {
        return transloco.translate(key, params, BASE_LANGUAGE);
      }
    }
    return key;
  }
}
