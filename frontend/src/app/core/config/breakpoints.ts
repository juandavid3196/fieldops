/**
 * Script mirrors of `styles/_breakpoints.scss` (SCSS emits no CSS a script can read).
 * Keep both files in sync: md 48rem, admin 68.75rem (1100px), dock 90rem (1440px).
 */
export const MD_QUERY = '(min-width: 48rem)';
export const ADMIN_QUERY = '(min-width: 68.75rem)';
export const DOCK_QUERY = '(min-width: 90rem)';

/** Subscribes to a media query; returns an unsubscribe function. No-op where `matchMedia` is missing. */
export function watchMedia(
  query: string,
  onChange: (matches: boolean) => void,
  fallback = true,
): { readonly matches: boolean; readonly stop: () => void } {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return { matches: fallback, stop: () => undefined };
  }
  const list = window.matchMedia(query);
  const listener = (event: MediaQueryListEvent): void => onChange(event.matches);
  list.addEventListener('change', listener);
  return { matches: list.matches, stop: () => list.removeEventListener('change', listener) };
}
