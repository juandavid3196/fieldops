import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
} from '@angular/core';
import {
  Translation,
  TranslocoLoader,
  TranslocoService,
  provideTransloco,
  provideTranslocoMissingHandler,
} from '@jsverse/transloco';
import { TitleStrategy } from '@angular/router';
import { Observable, of } from 'rxjs';

import { FieldOpsMissingHandler } from '../i18n-missing.handler';
import { scopeIdOf } from '../i18n-scopes';
import { TranslatedTitleStrategy } from '../translated-title.strategy';
import { Translator, interpolate } from '../translate';
import { AVAILABLE_LANGUAGE_CODES, BASE_LANGUAGE } from '../languages.config';

/** Feature folders that own an `i18n/` scope. Keep in sync with `features/<folder>/i18n`. */
export const I18N_FEATURE_FOLDERS = [
  'account',
  'audit-log',
  'authentication',
  'billing-review',
  'business-overview',
  'coming-soon',
  'customers',
  'dashboard',
  'help',
  'invoices',
  'jobs',
  'legal',
  'messages',
  'notifications',
  'organizations',
  'overview',
  'portal',
  'products-services',
  'quotes',
  'reports',
  'requests',
  'schedule',
  'service-request',
  'team',
  'technician-jobs',
  'users',
] as const;

const resources: Record<string, Translation> = {};

/** Loads the root and every feature scope once per test file, so tests render synchronously. */
export async function preloadI18nResources(): Promise<void> {
  for (const lang of AVAILABLE_LANGUAGE_CODES) {
    const root = (await import(`../${lang}.json`)).default as Translation;
    const merged: Translation = { ...root };
    for (const folder of I18N_FEATURE_FOLDERS) {
      try {
        merged[scopeIdOf(folder)] = (
          await import(`../../../features/${folder}/i18n/${lang}.json`)
        ).default;
      } catch {
        // A scope without a file for this language is reported by `npm run i18n:check`.
      }
    }
    resources[lang] = merged;
  }
}

class TestTranslationLoader implements TranslocoLoader {
  getTranslation(lang: string): Observable<Translation> {
    return of(resources[lang] ?? {});
  }
}

/**
 * Test providers: Transloco with every scope merged into the root translation and loaded
 * synchronously, English active. Registered for every test by `src/test-setup.ts`.
 */
export function provideI18nTesting(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideTransloco({
      config: {
        availableLangs: [...AVAILABLE_LANGUAGE_CODES],
        defaultLang: BASE_LANGUAGE,
        fallbackLang: BASE_LANGUAGE,
        failedRetries: 0,
        reRenderOnLangChange: true,
        prodMode: true,
        missingHandler: { useFallbackTranslation: false, logMissingKey: false },
      },
      loader: TestTranslationLoader,
    }),
    provideTranslocoMissingHandler(FieldOpsMissingHandler),
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    provideEnvironmentInitializer(() => {
      const transloco = inject(TranslocoService);
      for (const lang of AVAILABLE_LANGUAGE_CODES) {
        transloco.load(lang).subscribe();
      }
      transloco.setActiveLang(BASE_LANGUAGE);
    }),
  ]);
}

function flatten(
  source: Translation,
  prefix = '',
  out: Record<string, string> = {},
): Record<string, string> {
  for (const [key, value] of Object.entries(source)) {
    const path = prefix === '' ? key : `${prefix}.${key}`;
    if (typeof value === 'string') {
      out[path] = value;
    } else if (value !== null && typeof value === 'object') {
      flatten(value as Translation, path, out);
    }
  }
  return out;
}

/**
 * DI-free translator over the preloaded resources, for tests of pure utilities that call `t()`
 * without a `TestBed`. A test that creates `I18n` replaces it for the rest of that test.
 */
export function createStaticTranslator(lang = 'en'): Translator {
  const flat = flatten(resources[lang] ?? {});
  let misses = 0;
  return {
    translate: (key, params) => {
      const value = flat[key];
      if (value === undefined) {
        misses++;
        return key;
      }
      return interpolate(value, params);
    },
    has: (key) => flat[key] !== undefined,
    language: () => lang,
    misses: () => misses,
  };
}
