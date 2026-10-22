import { activeIntlLocale } from './active-locale';

/** CLDR plural category of a count in the active locale (`one` or `other` for en and es). */
export function pluralCategory(count: number): 'one' | 'other' {
  return new Intl.PluralRules(activeIntlLocale()).select(count) === 'one' ? 'one' : 'other';
}

/** Key of the plural form: `<base>.one` or `<base>.other`. */
export function pluralKey(base: string, count: number): string {
  return `${base}.${pluralCategory(count)}`;
}
