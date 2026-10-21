import { TestBed } from '@angular/core/testing';

import { setActiveIntlLocale } from './app/core/i18n/active-locale';
import { LANGUAGE_STORAGE_KEY } from './app/core/i18n/languages.config';
import {
  createStaticTranslator,
  preloadI18nResources,
  provideI18nTesting,
} from './app/core/i18n/testing/i18n-testing';
import { registerTranslator } from './app/core/i18n/translate';

// A test that resets the module itself still gets the translation providers (English, real files).
const resetTestingModule = TestBed.resetTestingModule.bind(TestBed);
TestBed.resetTestingModule = () => {
  const result = resetTestingModule();
  TestBed.configureTestingModule({ providers: [provideI18nTesting()] });
  return result;
};

// Translations are loaded before any spec module runs, so constants evaluated at import time
// (test tables, module-level labels) already read English text.
await preloadI18nResources();
registerTranslator(createStaticTranslator('en'));

// Every test starts in English with the real translation files (app-i18n-en-es BR-01).
beforeEach(() => {
  setActiveIntlLocale('en-US');
  registerTranslator(createStaticTranslator('en'));
  document.documentElement.lang = 'en';
  try {
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  } catch {
    // Storage may be unavailable in a test that replaced it.
  }
  TestBed.resetTestingModule();
});
