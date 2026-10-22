/** BR-01: the single frontend list of supported appearance preferences (default `system`). */
export const THEME_PREFERENCES = ['system', 'light', 'dark'] as const;

export type ThemePreference = (typeof THEME_PREFERENCES)[number];
export type ThemeMode = 'light' | 'dark';

export const DEFAULT_THEME_PREFERENCE: ThemePreference = 'system';

/** BR-03: the dedicated `localStorage` key holding the last applied preference. */
export const THEME_STORAGE_KEY = 'fieldops.theme';

/** BR-04: `<meta name="theme-color">` per effective mode. */
export const THEME_COLORS: Readonly<Record<ThemeMode, string>> = {
  light: '#0f9d9a',
  dark: '#0b1620',
};

/** BR-04: the stable class on `<html>`, set only in dark mode (PrimeNG `darkModeSelector`). */
export const DARK_CLASS = 'app-dark';

export function isSupportedTheme(value: unknown): value is ThemePreference {
  return typeof value === 'string' && (THEME_PREFERENCES as readonly string[]).includes(value);
}
