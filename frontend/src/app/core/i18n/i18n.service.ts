import { Injectable, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

import { missingKeyCount } from './i18n-missing.handler';
import { LanguageService } from './language.service';
import { pluralKey } from './plural';
import { TranslateParams, Translator, registerTranslator } from './translate';

export type I18nParams = TranslateParams;

/**
 * Translation facade for TypeScript and the bridge behind `t()`. Reading the language signal makes
 * every `computed` that calls `t` re-evaluate when the language changes, so TypeScript strings
 * follow the same switch as the template pipe. Pure format utilities return codes or keys (or call
 * `t`) and let the caller render them.
 */
@Injectable({ providedIn: 'root' })
export class I18n implements Translator {
  private readonly transloco = inject(TranslocoService);
  private readonly languageService = inject(LanguageService);

  constructor() {
    registerTranslator(this);
  }

  /** Translated text of a key (BR-11 behavior when missing). */
  t(key: string, params?: I18nParams): string {
    this.languageService.lang();
    return this.transloco.translate<string>(key, params);
  }

  translate(key: string, params?: I18nParams): string {
    return this.t(key, params);
  }

  language(): string {
    return this.languageService.lang();
  }

  misses(): number {
    return missingKeyCount();
  }

  /** Whether the active language has the key; never logs a warning. */
  has(key: string): boolean {
    const lang = this.languageService.lang();
    const value = this.transloco.getTranslation(lang)[key];
    return typeof value === 'string' && value !== '';
  }

  /** `<base>.one` / `<base>.other` by the active locale's plural rules; `count` is a param. */
  plural(base: string, count: number, params: I18nParams = {}): string {
    return this.t(pluralKey(base, count), { count, ...params });
  }

  /**
   * BR-14: label of a stable code (`<prefix>.<code>`); an unknown code yields `fallback` (the server
   * name or stored text) without a missing-key warning.
   */
  label(prefix: string, code: string | null | undefined, fallback: string): string {
    if (code === null || code === undefined || code === '') {
      return fallback;
    }
    const key = `${prefix}.${code}`;
    return this.has(key) ? this.t(key) : fallback;
  }
}
