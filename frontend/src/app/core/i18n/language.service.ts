import { DOCUMENT, registerLocaleData } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { PrimeNG } from 'primeng/config';
import { Observable, catchError, forkJoin, from, map, of, tap } from 'rxjs';

import { setActiveIntlLocale } from './active-locale';
import { I18nScopeRegistry } from './i18n-scopes';
import {
  BASE_LANGUAGE,
  LANGUAGE_STORAGE_KEY,
  LanguageCode,
  SUPPORTED_LANGUAGES,
  detectBrowserLanguage,
  isSupportedLanguage,
  localeOf,
} from './languages.config';

/**
 * Owns the active language (BR-01..BR-04, BR-19): resolution, the local value, `<html lang>`,
 * Intl locale, PrimeNG texts and the Transloco language. `apply` loads the root scope and every
 * activated feature scope first, so a switch never flashes unresolved keys (BR-20).
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly primeng = inject(PrimeNG);
  private readonly document = inject(DOCUMENT);
  private readonly scopes = inject(I18nScopeRegistry);
  private readonly active = signal<LanguageCode>(BASE_LANGUAGE);
  private latest = 0;

  readonly lang = this.active.asReadonly();
  readonly locale = computed(() => localeOf(this.active()));
  readonly options = SUPPORTED_LANGUAGES;

  /** BR-02/BR-03 without a session: local value, then browser language, then English. */
  resolveInitial(): LanguageCode {
    return this.readLocal() ?? detectBrowserLanguage(this.document.defaultView?.navigator);
  }

  /** Application initializer: the first screen renders in the resolved language. */
  init(): Observable<LanguageCode> {
    return this.apply(this.resolveInitial());
  }

  /** Applies a supported language; resolves once its texts are loaded. */
  apply(code: LanguageCode): Observable<LanguageCode> {
    if (!isSupportedLanguage(code)) {
      return of(this.active());
    }
    const run = ++this.latest;
    return forkJoin([this.loadRoot(code), this.scopes.loadAll(code), this.loadLocale(code)]).pipe(
      map(() => code),
      tap(() => {
        if (run === this.latest) {
          this.commit(code);
        }
      }),
    );
  }

  /**
   * BR-02: a session `preferredLanguage` wins; an unsupported value (or none) is ignored and the
   * current language stays. The local value follows the applied language (BR-04).
   */
  applyPreference(value: unknown): Observable<unknown> {
    if (!isSupportedLanguage(value)) {
      return of(null);
    }
    if (value === this.active()) {
      this.writeLocal(value);
      return of(value);
    }
    return this.apply(value);
  }

  /** Local value restore (BR-04); unreadable or unsupported values are ignored. */
  readLocal(): LanguageCode | null {
    try {
      const value = this.document.defaultView?.localStorage.getItem(LANGUAGE_STORAGE_KEY);
      return isSupportedLanguage(value) ? value : null;
    } catch {
      return null;
    }
  }

  private writeLocal(code: LanguageCode): void {
    try {
      this.document.defaultView?.localStorage.setItem(LANGUAGE_STORAGE_KEY, code);
    } catch {
      // BR-04: storage errors never break rendering.
    }
  }

  private commit(code: LanguageCode): void {
    this.transloco.setActiveLang(code);
    this.active.set(code);
    setActiveIntlLocale(localeOf(code));
    this.document.documentElement.lang = code;
    this.primeng.setTranslation(this.primeNgTexts(code));
    this.writeLocal(code);
  }

  private loadRoot(code: LanguageCode): Observable<unknown> {
    return this.transloco.load(code).pipe(
      catchError(() =>
        // BR-11: the root scope of the language failed; English keeps every screen readable.
        this.transloco.load(BASE_LANGUAGE).pipe(catchError(() => of(null))),
      ),
    );
  }

  private loadLocale(code: LanguageCode): Observable<unknown> {
    if (code !== 'es') {
      return of(null);
    }
    return from(import('@angular/common/locales/es-CO')).pipe(
      tap((module) => registerLocaleData(module.default, 'es-CO')),
      catchError(() => of(null)),
    );
  }

  /** BR-10: PrimeNG texts and the CLDR names of days and months for the active locale. */
  private primeNgTexts(code: LanguageCode): Parameters<PrimeNG['setTranslation']>[0] {
    const locale = localeOf(code);
    const texts = this.transloco.translateObject('primeng') as Record<string, unknown>;
    const names = (options: Intl.DateTimeFormatOptions, dates: readonly Date[]): string[] =>
      dates.map((date) =>
        capitalize(new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' }).format(date)),
      );
    // 2023-01-01 is a Sunday: index 0 is Sunday, matching `firstDayOfWeek: 0` for en-US and es-CO.
    const days = Array.from({ length: 7 }, (_, index) => new Date(Date.UTC(2023, 0, 1 + index)));
    const months = Array.from({ length: 12 }, (_, index) => new Date(Date.UTC(2023, index, 1)));
    return {
      ...(typeof texts === 'object' ? texts : {}),
      dayNames: names({ weekday: 'long' }, days),
      dayNamesShort: names({ weekday: 'short' }, days),
      dayNamesMin: names({ weekday: 'short' }, days).map((name) => name.slice(0, 2)),
      monthNames: names({ month: 'long' }, months),
      monthNamesShort: names({ month: 'short' }, months),
      firstDayOfWeek: 0,
    };
  }
}

function capitalize(value: string): string {
  return value.charAt(0).toLocaleUpperCase() + value.slice(1);
}
