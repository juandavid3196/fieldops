import { Component, DestroyRef, ElementRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Toast } from 'primeng/toast';
import { Observable, map, startWith } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { AccountService } from '../../services/account.service';
import {
  PasswordErrors,
  PasswordField,
  PasswordValues,
  passwordRequirements,
  tooManyAttemptsMessage,
  validatePassword,
} from '../../utils/account-validation';

import { localizedList, t } from '../../../../core/i18n/translate';
export const PASSWORD_ERROR = (): string => t('account.security.passwordError');
/** Fallback block when a `429` carries no readable `Retry-After`: the BR-06 window length. */
const DEFAULT_RETRY_AFTER_SECONDS = 900;

interface PasswordFieldConfig {
  readonly key: PasswordField;
  readonly label: string;
  /** Used in the toggle's accessible name: "Show current password". */
  readonly noun: string;
  readonly autocomplete: string;
}

const FIELD_CONFIGS: readonly PasswordFieldConfig[] = localizedList(() => [
  {
    key: 'currentPassword',
    label: t('account.security.currentPassword'),
    noun: t('account.security.nounCurrent'),
    autocomplete: 'current-password',
  },
  {
    key: 'newPassword',
    label: t('account.security.newPassword'),
    noun: t('account.security.nounNew'),
    autocomplete: 'new-password',
  },
  {
    key: 'confirmPassword',
    label: t('account.security.confirmNewPassword'),
    noun: t('account.security.nounConfirmation'),
    autocomplete: 'new-password',
  },
]);

const EMPTY: PasswordValues = { currentPassword: '', newPassword: '', confirmPassword: '' };

/** Security (FR-10, FR-11): change password while keeping this session. */
@Component({
  selector: 'app-security',
  imports: [
    TranslocoPipe,
    ReactiveFormsModule,
    ButtonDirective,
    InputText,
    Message,
    SpinnerIcon,
    Toast,
    DiscardChangesDialog,
  ],
  templateUrl: './security.html',
  styleUrl: './security.scss',
  providers: [MessageService, ConfirmationService],
})
export class Security {
  private readonly accountService = inject(AccountService);
  private readonly sessionService = inject(SessionService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly fields = FIELD_CONFIGS;
  get passwordErrorText(): string {
    return PASSWORD_ERROR();
  }

  readonly form = new FormGroup({
    currentPassword: new FormControl('', { nonNullable: true }),
    newPassword: new FormControl('', { nonNullable: true }),
    confirmPassword: new FormControl('', { nonNullable: true }),
  });

  private readonly values = toSignal(
    this.form.valueChanges.pipe(
      startWith(null),
      map(() => this.form.getRawValue()),
    ),
    { initialValue: EMPTY },
  );

  readonly requirements = computed(() =>
    passwordRequirements(this.values(), this.sessionService.session()?.user.email ?? ''),
  );
  readonly visible = signal<Record<PasswordField, boolean>>({
    currentPassword: false,
    newPassword: false,
    confirmPassword: false,
  });
  readonly fieldErrors = signal<PasswordErrors>({});
  readonly submitting = signal(false);
  readonly failed = signal(false);
  /** BR-10 `429` message; the submit stays disabled until the `Retry-After` period ends. */
  readonly rateLimitMessage = signal<string | null>(null);

  private sessionExpired = false;
  private unblockTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.clearUnblockTimer());
  }

  toggleVisible(field: PasswordField): void {
    this.visible.update((state) => ({ ...state, [field]: !state[field] }));
  }

  /** Consulted by `accountUnsavedChangesGuard` on route leave (FR-12). */
  canLeave(): boolean | Observable<boolean> {
    const value = this.form.getRawValue();
    if (this.sessionExpired || Object.values(value).every((part) => part === '')) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      const settle = (leave: boolean): void => {
        subscriber.next(leave);
        subscriber.complete();
      };
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: t('account.security.yourPassword'),
          accept: () => settle(true),
          reject: () => settle(false),
        }),
      );
    });
  }

  /** Clears the three fields and every error; sends nothing. A running `429` block stays. */
  cancel(): void {
    this.form.reset(EMPTY);
    this.fieldErrors.set({});
    this.failed.set(false);
  }

  submit(): void {
    if (this.submitting() || this.rateLimitMessage() !== null) {
      return;
    }
    this.failed.set(false);

    const value = this.form.getRawValue();
    const errors = validatePassword(value, this.sessionService.session()?.user.email ?? '');
    this.fieldErrors.set(errors);
    const invalid = FIELD_CONFIGS.find((field) => errors[field.key] !== undefined);
    if (invalid !== undefined) {
      this.host.querySelector<HTMLElement>(`#security-${invalid.key}`)?.focus();
      return;
    }

    this.submitting.set(true);
    this.accountService
      .changePassword({ currentPassword: value.currentPassword, newPassword: value.newPassword })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.form.reset(EMPTY);
          this.fieldErrors.set({});
          this.messageService.add({
            severity: 'success',
            summary: t('account.security.passwordUpdated'),
          });
        },
        error: (failure: unknown) => {
          this.submitting.set(false);
          this.handleFailure(failure);
        },
      });
  }

  private handleFailure(failure: unknown): void {
    if (isApiError(failure) && failure.status === 401) {
      this.sessionExpired = true;
      void this.router.navigateByUrl('/auth/sign-in', { replaceUrl: true });
      return;
    }
    if (isApiError(failure) && failure.status === 429) {
      this.block(failure.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS);
      return;
    }
    const known: PasswordErrors = {};
    if (isApiError(failure)) {
      for (const field of ['currentPassword', 'newPassword'] as const) {
        const message = failure.fieldErrors[field]?.[0];
        if (message !== undefined) {
          known[field] = message;
        }
      }
    }
    if (Object.keys(known).length > 0) {
      this.fieldErrors.set(known);
    } else {
      this.failed.set(true);
    }
  }

  private block(seconds: number): void {
    this.clearUnblockTimer();
    this.rateLimitMessage.set(tooManyAttemptsMessage(seconds));
    this.unblockTimer = setTimeout(() => {
      this.unblockTimer = null;
      this.rateLimitMessage.set(null);
    }, seconds * 1000);
  }

  private clearUnblockTimer(): void {
    if (this.unblockTimer !== null) {
      clearTimeout(this.unblockTimer);
      this.unblockTimer = null;
    }
  }
}
