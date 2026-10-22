import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

import { effectiveMode, resolvePreference } from './theme-resolver';
import {
  DARK_CLASS,
  DEFAULT_THEME_PREFERENCE,
  THEME_COLORS,
  THEME_STORAGE_KEY,
  ThemeMode,
  ThemePreference,
  isSupportedTheme,
} from './theme.config';

/**
 * Owns the theme (BR-02..BR-04): the preference, the effective mode and the document state.
 * `system` renders the light theme; the device color scheme is not followed. `apply` is DOM-only
 * (previews); `applyPreference` is the session value and `commit` a successful save; both also
 * write the local value (BR-03).
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<ThemePreference>(DEFAULT_THEME_PREFERENCE);
  private readonly effective = signal<ThemeMode>('light');
  private readonly persistedState = signal<ThemePreference>(DEFAULT_THEME_PREFERENCE);

  readonly preference = this.current.asReadonly();
  readonly mode = this.effective.asReadonly();
  /** The last saved preference: the Preferences page's saved value (BR-10, BR-13). */
  readonly persisted = this.persistedState.asReadonly();

  /** Application initializer: the local value, then `system` (BR-02 steps 2 and 3). */
  init(): void {
    const preference = resolvePreference(null, this.readLocal());
    this.persistedState.set(preference);
    this.apply(preference);
  }

  /** Applies a supported preference to the document without writing the local value. */
  apply(preference: ThemePreference): void {
    if (!isSupportedTheme(preference)) {
      return;
    }
    this.current.set(preference);
    this.render(effectiveMode(preference));
  }

  /** BR-02 step 1: a supported session value wins and becomes the local value; others are ignored. */
  applyPreference(value: unknown): void {
    if (isSupportedTheme(value)) {
      this.commit(value);
    }
  }

  /** A successful save or session value: applies, remembers as saved and writes locally. */
  commit(preference: ThemePreference): void {
    this.persistedState.set(preference);
    this.apply(preference);
    this.writeLocal(preference);
  }

  /** Failure restore (BR-12, BR-15): the previous appearance, the saved value stays local. */
  restore(previous: ThemePreference): void {
    this.apply(previous);
    this.writeLocal(this.persistedState());
  }

  readLocal(): ThemePreference | null {
    try {
      const value = this.document.defaultView?.localStorage.getItem(THEME_STORAGE_KEY);
      return isSupportedTheme(value) ? value : null;
    } catch {
      return null;
    }
  }

  private writeLocal(preference: ThemePreference): void {
    try {
      this.document.defaultView?.localStorage.setItem(THEME_STORAGE_KEY, preference);
    } catch {
      // BR-03: storage errors never break rendering.
    }
  }

  /** BR-04: `app-dark` class, `color-scheme` and `theme-color` always equal the effective mode. */
  private render(mode: ThemeMode): void {
    this.effective.set(mode);
    const root = this.document.documentElement;
    root.classList.toggle(DARK_CLASS, mode === 'dark');
    root.style.colorScheme = mode;
    this.document
      .querySelector('meta[name="theme-color"]')
      ?.setAttribute('content', THEME_COLORS[mode]);
  }
}
