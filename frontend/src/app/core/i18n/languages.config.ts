/**
 * BR-01: the single frontend list of supported languages. A later language is added here, in the
 * backend list and with its translation files; names are always shown in their own language.
 */
export const SUPPORTED_LANGUAGES = [
  { code: 'en', locale: 'en-US', name: 'English' },
  { code: 'es', locale: 'es-CO', name: 'Español' },
] as const;

export type LanguageCode = (typeof SUPPORTED_LANGUAGES)[number]['code'];
export type LanguageOption = (typeof SUPPORTED_LANGUAGES)[number];

/** BR-01: base language and global fallback. */
export const BASE_LANGUAGE: LanguageCode = 'en';

/** BR-04: the dedicated `localStorage` key holding the last effective language. */
export const LANGUAGE_STORAGE_KEY = 'fieldops.language';

export const AVAILABLE_LANGUAGE_CODES: readonly LanguageCode[] = SUPPORTED_LANGUAGES.map(
  (language) => language.code,
);

export function isSupportedLanguage(value: unknown): value is LanguageCode {
  return typeof value === 'string' && AVAILABLE_LANGUAGE_CODES.includes(value as LanguageCode);
}

export function languageOption(code: LanguageCode): LanguageOption {
  return SUPPORTED_LANGUAGES.find((language) => language.code === code) ?? SUPPORTED_LANGUAGES[0];
}

export function localeOf(code: LanguageCode): string {
  return languageOption(code).locale;
}

/**
 * BR-03: the first entry of `navigator.languages` (or `navigator.language` when the list is empty)
 * decides; a value starting with `es` (any case) is Spanish, anything else or nothing is English.
 */
export function detectBrowserLanguage(
  nav: Pick<Navigator, 'languages' | 'language'> | undefined,
): LanguageCode {
  const first = nav?.languages?.length ? nav.languages[0] : nav?.language;
  return typeof first === 'string' && first.toLowerCase().startsWith('es') ? 'es' : BASE_LANGUAGE;
}
