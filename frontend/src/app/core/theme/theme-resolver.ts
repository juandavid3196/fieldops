import {
  DEFAULT_THEME_PREFERENCE,
  ThemeMode,
  ThemePreference,
  isSupportedTheme,
} from './theme.config';

/** BR-02 preference: the session value, then the local value, then `system`; unsupported is skipped. */
export function resolvePreference(session: unknown, local: unknown): ThemePreference {
  if (isSupportedTheme(session)) {
    return session;
  }
  return isSupportedTheme(local) ? local : DEFAULT_THEME_PREFERENCE;
}

/** Effective mode: `system` is the light theme (product decision); `light` and `dark` are as chosen. */
export function effectiveMode(preference: ThemePreference): ThemeMode {
  return preference === 'system' ? 'light' : preference;
}
