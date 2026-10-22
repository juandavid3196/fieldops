/**
 * Translation entry points for TypeScript and pure utilities.
 *
 * `t(key, params)` returns the text of the active language and reads the language signal, so any
 * `computed`, effect or template that calls it re-evaluates when the language changes (BR-04 of
 * the spec FR-04). The translator is registered by `I18n` (and by the test setup); without one the
 * key is returned.
 *
 * `localized` / `localizedList` wrap a constant (object or array) whose texts come from `t`: the
 * value is built lazily and rebuilt once per language, so module and field constants stay
 * drop-in values that follow the language switch.
 */
export type TranslateParams = Record<string, unknown>;

export interface Translator {
  translate(key: string, params?: TranslateParams): string;
  has(key: string): boolean;
  /** Active language code; reads the language signal. */
  language(): string;
  /** Number of missing-key lookups so far (a constant built with misses is not cached). */
  misses(): number;
}

let translator: Translator | null = null;

export function registerTranslator(value: Translator | null): void {
  translator = value;
}

export function t(key: string, params?: TranslateParams): string {
  return translator ? translator.translate(key, params) : key;
}

/** Whether the active language has the key; never warns. */
export function hasTranslation(key: string): boolean {
  return translator?.has(key) ?? false;
}

/** The translation of `value` when it is a translation key, otherwise `value` itself. */
export function tOrRaw(value: string): string {
  return hasTranslation(value) ? t(value) : value;
}

/** BR-14: translated role name by its code; an unknown code keeps the server name. */
export function tRole(code: string, fallback: string): string {
  return hasTranslation(`roles.${code}`) ? t(`roles.${code}`) : fallback;
}

/** BR-14: label of a stable code (`<prefix>.<code>`); an unknown code keeps `fallback`. */
export function tCode(prefix: string, code: string, fallback: string): string {
  const key = `${prefix}.${code}`;
  return hasTranslation(key) ? t(key) : fallback;
}

/** Active language code (reads the signal). */
export function activeLanguageCode(): string {
  return translator?.language() ?? 'en';
}

/** Interpolates `{{name}}` placeholders; shared by the test translator. */
export function interpolate(text: string, params?: TranslateParams): string {
  return params
    ? text.replace(/\{\{\s*([\w$]+)\s*\}\}/g, (match, name: string) =>
        name in params ? String(params[name] ?? '') : match,
      )
    : text;
}

function memoized<T extends object>(factory: () => T): () => T {
  let cached: { lang: string; value: T } | null = null;
  return () => {
    if (translator === null) {
      // Evaluated before a translator exists (module load): never cache keys.
      return factory();
    }
    const lang = activeLanguageCode();
    if (cached?.lang === lang) {
      return cached.value;
    }
    const before = translator?.misses() ?? 0;
    const value = factory();
    // A constant built while a scope was not loaded yet is rebuilt on the next access.
    cached = (translator?.misses() ?? 0) === before ? { lang, value } : null;
    return value;
  };
}

function proxyOf<T extends object>(target: object, resolve: () => T): T {
  const configurable = (descriptor: PropertyDescriptor | undefined) =>
    descriptor === undefined ? undefined : { ...descriptor, configurable: true };
  return new Proxy(target, {
    get: (_, property) => {
      const value = resolve();
      const result = Reflect.get(value, property, value);
      return typeof result === 'function' ? result.bind(value) : result;
    },
    has: (_, property) => Reflect.has(resolve(), property),
    ownKeys: () => Reflect.ownKeys(resolve()),
    getOwnPropertyDescriptor: (_, property) => {
      const descriptor = Reflect.getOwnPropertyDescriptor(resolve(), property);
      return property === 'length' && Array.isArray(resolve())
        ? descriptor
        : configurable(descriptor);
    },
    getPrototypeOf: () => Reflect.getPrototypeOf(resolve()),
    set: () => false,
    defineProperty: () => false,
    deleteProperty: () => false,
  }) as T;
}

/** Object constant with translated texts, rebuilt once per language. */
export function localized<T extends object>(factory: () => T): T {
  return proxyOf<T>({}, memoized(factory));
}

/** Array constant with translated texts, rebuilt once per language. */
export function localizedList<T extends readonly unknown[]>(factory: () => T): T {
  return proxyOf<T>([], memoized(factory));
}
