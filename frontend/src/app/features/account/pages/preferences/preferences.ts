import { Component, ElementRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Select } from 'primeng/select';
import { Toast } from 'primeng/toast';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { t } from '../../../../core/i18n/translate';
import { LanguagePreferenceService } from '../../../../core/i18n/language-preference.service';
import { LanguageService } from '../../../../core/i18n/language.service';
import { LanguageCode } from '../../../../core/i18n/languages.config';
import { isApiError } from '../../../../core/models/api-error.model';
import { ThemePreferenceService } from '../../../../core/theme/theme-preference.service';
import { THEME_PREFERENCES, ThemePreference } from '../../../../core/theme/theme.config';
import { ThemeService } from '../../../../core/theme/theme.service';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';

interface AppearanceCard {
  readonly value: ThemePreference;
  readonly icon: string;
  readonly labelKey: string;
  readonly descriptionKey: string;
}

const CARDS: readonly AppearanceCard[] = [
  {
    value: 'system',
    icon: 'pi-desktop',
    labelKey: 'selector.themeSystem',
    descriptionKey: 'account.preferences.systemDescription',
  },
  {
    value: 'light',
    icon: 'pi-sun',
    labelKey: 'selector.themeLight',
    descriptionKey: 'account.preferences.lightDescription',
  },
  {
    value: 'dark',
    icon: 'pi-moon',
    labelKey: 'selector.themeDark',
    descriptionKey: 'account.preferences.darkDescription',
  },
];

type SaveResult = 'unchanged' | 'saved' | 'failed' | 'unauthorized';

/**
 * Preferences (app-dark-mode FR-05..FR-09): Language and Appearance with an immediate preview,
 * Save preferences (changed values only, concurrently) and Reset. The saved values are the ones
 * held by the language and theme services, so a menu change updates them too (BR-13).
 */
@Component({
  selector: 'app-preferences',
  imports: [
    TranslocoPipe,
    FormsModule,
    ButtonDirective,
    Select,
    SpinnerIcon,
    Toast,
    DiscardChangesDialog,
  ],
  templateUrl: './preferences.html',
  styleUrl: './preferences.scss',
  providers: [MessageService, ConfirmationService],
})
export class Preferences {
  private readonly language = inject(LanguageService);
  private readonly languagePreference = inject(LanguagePreferenceService);
  private readonly theme = inject(ThemeService);
  private readonly themePreference = inject(ThemePreferenceService);
  private readonly messages = inject(MessageService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly router = inject(Router);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private sessionExpired = false;

  readonly languages = [...this.language.options];
  readonly savedLanguage = this.language.persisted;
  readonly savedTheme = this.theme.persisted;
  readonly selectedLanguage = signal<LanguageCode>(this.language.persisted());
  readonly selectedTheme = signal<ThemePreference>(this.theme.persisted());
  readonly saving = signal(false);

  readonly dirty = computed(
    () =>
      this.selectedLanguage() !== this.savedLanguage() ||
      this.selectedTheme() !== this.savedTheme(),
  );

  readonly cards = CARDS;

  constructor() {
    // BR-13: a language or appearance saved elsewhere (a shell menu) becomes the selected value.
    effect(() => {
      const saved = this.savedLanguage();
      untracked(() => this.selectedLanguage.set(saved));
    });
    effect(() => {
      const saved = this.savedTheme();
      untracked(() => this.selectedTheme.set(saved));
    });
  }

  /** BR-10: previews the language at once, without a request or a local write. */
  selectLanguage(code: LanguageCode): void {
    if (this.saving() || code === this.selectedLanguage()) {
      return;
    }
    this.selectedLanguage.set(code);
    this.language.apply(code, false).subscribe();
  }

  /** BR-10: previews the appearance at once, without a request or a local write. */
  selectTheme(value: ThemePreference): void {
    if (this.saving() || value === this.selectedTheme()) {
      return;
    }
    this.selectedTheme.set(value);
    this.theme.apply(value);
  }

  /** WAI-ARIA radio group: arrows move and select, Space selects. */
  onCardKeydown(event: KeyboardEvent, index: number): void {
    const count = THEME_PREFERENCES.length;
    let next: number;
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        next = (index + 1) % count;
        break;
      case 'ArrowLeft':
      case 'ArrowUp':
        next = (index + count - 1) % count;
        break;
      case ' ':
        event.preventDefault();
        this.selectTheme(THEME_PREFERENCES[index]);
        return;
      default:
        return;
    }
    event.preventDefault();
    this.selectTheme(THEME_PREFERENCES[next]);
    this.host.querySelectorAll<HTMLElement>('[role="radio"]')[next]?.focus();
  }

  /** FR-07: applies the saved values and makes the page pristine. */
  reset(): void {
    if (this.saving()) {
      return;
    }
    this.discardPreview().subscribe();
  }

  /** FR-08: saves only the changed preferences, concurrently; failures restore only themselves. */
  save(): void {
    if (this.saving() || !this.dirty()) {
      return;
    }
    const code = this.selectedLanguage();
    const preference = this.selectedTheme();
    this.saving.set(true);

    const language$: Observable<SaveResult> =
      code === this.savedLanguage()
        ? of('unchanged')
        : this.languagePreference.save(code, 'internal').pipe(
            map((): SaveResult => 'saved'),
            catchError((failure: unknown) => of(resultOf(failure))),
          );
    const theme$: Observable<SaveResult> =
      preference === this.savedTheme()
        ? of('unchanged')
        : this.themePreference.save(preference, 'internal', 'preferences_page').pipe(
            map((): SaveResult => 'saved'),
            catchError((failure: unknown) => of(resultOf(failure))),
          );

    forkJoin([language$, theme$])
      .pipe(
        switchMap(([languageResult, themeResult]) => {
          const languageFailed = languageResult === 'failed' || languageResult === 'unauthorized';
          const themeFailed = themeResult === 'failed' || themeResult === 'unauthorized';
          if (themeFailed) {
            // BR-12: back to the saved appearance, UI and local value.
            this.theme.commit(this.savedTheme());
            this.selectedTheme.set(this.savedTheme());
          }
          const restored$: Observable<unknown> = languageFailed
            ? this.language.apply(this.savedLanguage()).pipe(catchError(() => of(null)))
            : of(null);
          return restored$.pipe(
            map(() => {
              if (languageFailed) {
                this.selectedLanguage.set(this.savedLanguage());
              }
              return { languageFailed, themeFailed, results: [languageResult, themeResult] };
            }),
          );
        }),
      )
      .subscribe(({ languageFailed, themeFailed, results }) => {
        this.saving.set(false);
        this.toast(languageFailed, themeFailed);
        if (results.includes('unauthorized')) {
          this.sessionExpired = true;
          void this.router.navigateByUrl('/auth/sign-in', { replaceUrl: true });
        }
      });
  }

  /** Consulted by `accountUnsavedChangesGuard` on route leave (BR-13). */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired || !this.dirty()) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      const settle = (leave: boolean): void => {
        subscriber.next(leave);
        subscriber.complete();
      };
      this.confirmation.confirm(
        discardChangesConfirmation({
          subject: t('account.preferences.yourPreferences'),
          accept: () => this.discardPreview().subscribe(() => settle(true)),
          reject: () => settle(false),
        }),
      );
    });
  }

  private discardPreview(): Observable<boolean> {
    this.theme.apply(this.savedTheme());
    this.selectedTheme.set(this.savedTheme());
    this.selectedLanguage.set(this.savedLanguage());
    return this.language.apply(this.savedLanguage(), false).pipe(
      map(() => true),
      catchError(() => of(true)),
    );
  }

  /** BR-09 / BR-12: success, or the failure toast for what failed, in the active language. */
  private toast(languageFailed: boolean, themeFailed: boolean): void {
    if (!languageFailed && !themeFailed) {
      this.messages.add({
        severity: 'success',
        summary: t('account.preferences.preferencesSaved'),
      });
      return;
    }
    const key =
      languageFailed && themeFailed
        ? 'preferencesSaveError'
        : languageFailed
          ? 'languageSaveError'
          : 'appearanceSaveError';
    this.messages.add({ severity: 'error', summary: t(`account.preferences.${key}`), life: 6000 });
  }
}

function resultOf(failure: unknown): SaveResult {
  return isApiError(failure) && failure.status === 401 ? 'unauthorized' : 'failed';
}
