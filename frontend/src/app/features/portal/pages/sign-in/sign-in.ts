import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputPassword } from 'primeng/inputpassword';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';

import { isApiError } from '../../../../core/models/api-error.model';
import { PortalSessionService } from '../../../../core/services/portal-session.service';
import {
  GENERIC_SIGN_IN_ERROR_MESSAGE,
  INVALID_CREDENTIALS_MESSAGE,
  rateLimitedMessage,
} from '../../../authentication/pages/sign-in/sign-in.messages';
import {
  SignInFieldErrors,
  mapServerFieldErrors,
  validateEmail,
  validatePassword,
} from '../../../authentication/pages/sign-in/sign-in.validators';
import { SESSION_ENDED_MESSAGE } from '../../models/portal.model';

const DEFAULT_RETRY_AFTER_SECONDS = 60;

/** Portal sign-in (BR-03): the internal sign-in messages and validation, a portal session. */
@Component({
  selector: 'app-portal-sign-in',
  imports: [
    FormsModule,
    ReactiveFormsModule,
    ButtonDirective,
    Checkbox,
    InputPassword,
    InputText,
    Message,
    RouterLink,
    SpinnerIcon,
  ],
  templateUrl: './sign-in.html',
  styleUrl: '../../portal-auth.scss',
})
export class PortalSignIn {
  private readonly sessions = inject(PortalSessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private retryTimer: ReturnType<typeof setTimeout> | undefined;
  private submitAttempted = false;

  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true }),
    rememberMe: new FormControl(false, { nonNullable: true }),
  });

  readonly fieldErrors = signal<SignInFieldErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly passwordMasked = signal(true);
  readonly retryLocked = signal(false);
  readonly submitDisabled = computed(() => this.submitting() || this.retryLocked());

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.retryTimer));
    // "Your session ended" shows once, then the notice is cleared (a cold visit shows none).
    if (this.sessions.expiredNotice()) {
      this.notice.set(SESSION_ENDED_MESSAGE);
      this.sessions.clearExpiredNotice();
    }
  }

  validateOnBlur(field: 'email' | 'password'): void {
    if (!this.submitAttempted) {
      return;
    }
    const { email, password } = this.form.getRawValue();
    const message = field === 'email' ? validateEmail(email) : validatePassword(password);
    this.fieldErrors.update((errors) => {
      const next = { ...errors };
      if (message === null) {
        delete next[field];
      } else {
        next[field] = message;
      }
      return next;
    });
  }

  submit(): void {
    if (this.submitDisabled()) {
      return;
    }
    this.submitAttempted = true;
    this.pageMessage.set(null);
    this.notice.set(null);

    const { email, password } = this.form.getRawValue();
    const errors: SignInFieldErrors = {};
    const emailError = validateEmail(email);
    const passwordError = validatePassword(password);
    if (emailError !== null) {
      errors.email = emailError;
    }
    if (passwordError !== null) {
      errors.password = passwordError;
    }
    this.fieldErrors.set(errors);
    if (emailError !== null || passwordError !== null) {
      return;
    }

    this.submitting.set(true);
    this.sessions
      .signIn(this.form.getRawValue())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          // The submitting state stays until the navigation replaces this page.
          this.router.navigateByUrl('/portal', { replaceUrl: true }).then(
            (navigated) => (navigated ? undefined : this.submitting.set(false)),
            () => this.submitting.set(false),
          );
        },
        error: (error: unknown) => this.failed(error),
      });
  }

  private failed(error: unknown): void {
    this.submitting.set(false);
    this.form.controls.password.setValue('');
    const apiError = isApiError(error) ? error : null;

    if (apiError?.kind === 'validation') {
      const errors = mapServerFieldErrors(apiError.fieldErrors);
      if (errors.email !== undefined || errors.password !== undefined) {
        this.fieldErrors.set(errors);
        return;
      }
    } else if (apiError?.kind === 'unauthorized') {
      this.pageMessage.set(INVALID_CREDENTIALS_MESSAGE);
      return;
    } else if (apiError?.kind === 'rate-limited') {
      const seconds = apiError.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS;
      this.pageMessage.set(rateLimitedMessage(Math.max(1, Math.ceil(seconds / 60))));
      clearTimeout(this.retryTimer);
      this.retryLocked.set(true);
      this.retryTimer = setTimeout(() => this.retryLocked.set(false), seconds * 1000);
      return;
    }
    this.pageMessage.set(GENERIC_SIGN_IN_ERROR_MESSAGE);
  }
}
