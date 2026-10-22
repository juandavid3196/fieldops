import { signal } from '@angular/core';

const activeLocale = signal('en-US');

/**
 * BR-10: the Intl locale of the active language (`en-US` or `es-CO`). Backed by a signal so any
 * computed, effect or template that formats a value re-renders when the language changes.
 */
export function activeIntlLocale(): string {
  return activeLocale();
}

/** Used by `LanguageService` only. */
export function setActiveIntlLocale(locale: string): void {
  activeLocale.set(locale);
}
