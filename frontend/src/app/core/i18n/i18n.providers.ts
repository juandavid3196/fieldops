import {
  EnvironmentProviders,
  Provider,
  inject,
  isDevMode,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { TitleStrategy } from '@angular/router';
import { provideTransloco, provideTranslocoMissingHandler } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { FieldOpsMissingHandler } from './i18n-missing.handler';
import { I18n } from './i18n.service';
import { RootTranslationLoader } from './i18n.loader';
import { LanguageService } from './language.service';
import { AVAILABLE_LANGUAGE_CODES, BASE_LANGUAGE } from './languages.config';
import { TranslatedTitleStrategy } from './translated-title.strategy';

/**
 * Registers Transloco (BR-01, BR-11, BR-20), the document title strategy and the initializer that
 * resolves and applies the initial language before the first screen renders (FR-01).
 */
export function provideI18n(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideTransloco({
      config: {
        availableLangs: [...AVAILABLE_LANGUAGE_CODES],
        defaultLang: BASE_LANGUAGE,
        fallbackLang: BASE_LANGUAGE,
        failedRetries: 1,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
        missingHandler: { useFallbackTranslation: false, logMissingKey: false },
      },
      loader: RootTranslationLoader,
    }),
    provideTranslocoMissingHandler(FieldOpsMissingHandler),
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy } satisfies Provider,
    provideAppInitializer(() => {
      inject(I18n);
      return firstValueFrom(inject(LanguageService).init());
    }),
  ]);
}
