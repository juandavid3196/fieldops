import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, map, of, tap } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { ThemePreference } from './theme.config';
import { ThemeService } from './theme.service';

/** Where the choice is made; each source is accepted only by its own endpoint (BR-06). */
export type ThemeSource = 'preferences_page' | 'user_menu' | 'technician_menu' | 'portal_menu';

export type ThemeSurface = 'internal' | 'portal';

/**
 * Applies an appearance choice and saves it through `PUT /account/theme` or
 * `PUT /portal/account/theme`. A menu failure restores the previous preference and local value and
 * raises {@link failures} for the shell toast (BR-15).
 */
@Injectable({ providedIn: 'root' })
export class ThemePreferenceService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly theme = inject(ThemeService);
  private readonly savingState = signal(false);
  private readonly failureCount = signal(0);

  /** `true` while a menu save is in flight: options are `aria-disabled` and repeats do nothing. */
  readonly saving = this.savingState.asReadonly();
  /** Incremented after every restored failure; the shell toast reacts to it. */
  readonly failures = this.failureCount.asReadonly();

  /** Menu choice (BR-15): apply at once, save, restore on failure. */
  choose(preference: ThemePreference, surface: ThemeSurface, source: ThemeSource): void {
    if (this.savingState() || preference === this.theme.preference()) {
      return;
    }
    const previous = this.theme.preference();
    this.savingState.set(true);
    this.theme.apply(preference);
    this.save(preference, surface, source)
      .pipe(
        catchError(() => {
          this.theme.restore(previous);
          this.failureCount.update((count) => count + 1);
          return of(undefined);
        }),
        finalize(() => this.savingState.set(false)),
      )
      .subscribe();
  }

  /** The request itself; a `200` makes the value the saved one and writes the local value. */
  save(preference: ThemePreference, surface: ThemeSurface, source: ThemeSource): Observable<void> {
    const path = surface === 'portal' ? 'portal/account/theme' : 'account/theme';
    return this.http
      .put<{ preferredTheme: string }>(buildApiUrl(this.config, path), {
        theme: preference,
        source,
      })
      .pipe(
        tap(() => this.theme.commit(preference)),
        map(() => undefined),
      );
  }
}
