import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, map, of, switchMap, tap } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { LanguageService } from './language.service';
import { LanguageCode } from './languages.config';

/** Where the choice is made: a session persists it through its own endpoint (BR-09). */
export type LanguageSurface = 'internal' | 'portal' | 'anonymous';

/**
 * Applies a language choice from a selector. A signed-in surface saves it through
 * `PUT /account/language` or `PUT /portal/account/language`; any failure restores the previous
 * language and local value and raises {@link failures} for the shell toast (BR-09).
 */
@Injectable({ providedIn: 'root' })
export class LanguagePreferenceService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly language = inject(LanguageService);
  private readonly savingState = signal(false);
  private readonly failureCount = signal(0);

  /** `true` while a save is in flight: options are `aria-disabled` and repeat choices do nothing. */
  readonly saving = this.savingState.asReadonly();
  /** Incremented after every restored failure; the shell toast reacts to it. */
  readonly failures = this.failureCount.asReadonly();

  choose(code: LanguageCode, surface: LanguageSurface): void {
    if (this.savingState() || code === this.language.lang()) {
      return;
    }

    const previous = this.language.lang();
    this.savingState.set(true);
    const apply$ = this.language.apply(code);
    if (surface === 'anonymous') {
      apply$.pipe(finalize(() => this.savingState.set(false))).subscribe();
      return;
    }

    apply$
      .pipe(
        switchMap(() => this.save(code, surface)),
        catchError(() =>
          this.language.apply(previous).pipe(
            map(() => {
              this.failureCount.update((count) => count + 1);
              return undefined;
            }),
            catchError(() => of(undefined)),
          ),
        ),
        finalize(() => this.savingState.set(false)),
      )
      .subscribe();
  }

  /**
   * `PUT /account/language` or `/portal/account/language`; a `200` makes the language the saved
   * one and writes the local value. Used by the menus and by the Preferences page.
   */
  save(code: LanguageCode, surface: Exclude<LanguageSurface, 'anonymous'>): Observable<void> {
    const path = surface === 'portal' ? 'portal/account/language' : 'account/language';
    return this.http
      .put<{ preferredLanguage: string }>(buildApiUrl(this.config, path), { language: code })
      .pipe(
        tap(() => this.language.markPersisted(code)),
        map(() => undefined),
      );
  }
}
