import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputPassword } from 'primeng/inputpassword';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { SignInBrandPanel } from '../../components/sign-in-brand-panel/sign-in-brand-panel';
import {
  GENERIC_SIGN_IN_ERROR_MESSAGE,
  INVALID_CREDENTIALS_MESSAGE,
  REGISTERED_MESSAGE,
  rateLimitedMessage,
} from './sign-in.messages';
import {
  SignInField,
  SignInFieldErrors,
  mapServerFieldErrors,
  validateEmail,
  validatePassword,
} from './sign-in.validators';

interface PageMessage {
  readonly severity: 'success' | 'error';
  readonly text: string;
}

/** Retry lock after a `429` without a usable `Retry-After` (FR-15). */
const DEFAULT_RETRY_AFTER_SECONDS = 60;

@Component({
  selector: 'app-sign-in',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonDirective,
    Checkbox,
    InputPassword,
    InputText,
    Message,
    SpinnerIcon,
    SignInBrandPanel,
  ],
  templateUrl: './sign-in.html',
  styleUrl: './sign-in.scss',
})
export class SignIn {
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  private readonly emailInput = viewChild.required<ElementRef<HTMLInputElement>>('emailInput');
  private readonly passwordInput =
    viewChild.required<ElementRef<HTMLInputElement>>('passwordInput');
  private readonly submitButton = viewChild.required<ElementRef<HTMLButtonElement>>('submitButton');

  private submitAttempted = false;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true }),
    rememberMe: new FormControl(false, { nonNullable: true }),
  });

  /** Displayed field messages; recomputed on submit and on blur after the first submit. */
  readonly fieldErrors = signal<SignInFieldErrors>({});
  readonly pageMessage = signal<PageMessage | null>(null);
  readonly submitting = signal(false);
  readonly retryLocked = signal(false);
  readonly passwordMasked = signal(true);
  readonly submitDisabled = computed(() => this.submitting() || this.retryLocked());

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.retryTimer));

    if (this.route.snapshot.queryParamMap.get('registered') === 'true') {
      this.pageMessage.set({ severity: 'success', text: REGISTERED_MESSAGE });
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: {},
        replaceUrl: true,
      });
    }
  }

  togglePassword(): void {
    this.passwordMasked.update((masked) => !masked);
  }

  validateOnBlur(field: SignInField): void {
    if (!this.submitAttempted) {
      return;
    }

    const message = this.validateField(field);
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

    const errors = this.validateForm();
    this.fieldErrors.set(errors);
    if (errors.email !== undefined || errors.password !== undefined) {
      this.focusFirstInvalid(errors);
      return;
    }

    this.submitting.set(true);
    this.sessionService
      .signIn(this.form.getRawValue())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.handleSignedIn(),
        error: (error: unknown) => this.handleSignInError(error),
      });
  }

  private handleSignedIn(): void {
    // The submitting state stays until the navigation replaces this page.
    this.router.navigateByUrl('/overview', { replaceUrl: true }).then(
      (navigated) => {
        if (!navigated) {
          this.submitting.set(false);
        }
      },
      () => this.submitting.set(false),
    );
  }

  private handleSignInError(error: unknown): void {
    this.submitting.set(false);
    this.form.controls.password.setValue('');

    const apiError: ApiError | null = isApiError(error) ? error : null;

    switch (apiError?.kind) {
      case 'validation': {
        const errors = mapServerFieldErrors(apiError.fieldErrors);
        if (errors.email !== undefined || errors.password !== undefined) {
          this.fieldErrors.set(errors);
          this.focusFirstInvalid(errors);
          return;
        }
        break;
      }
      case 'unauthorized':
        this.showError(INVALID_CREDENTIALS_MESSAGE);
        this.focusAfterRender(this.passwordInput);
        return;
      case 'rate-limited':
        this.lockAfterRateLimit(apiError.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS);
        this.focusAfterRender(this.emailInput);
        return;
    }

    this.showError(GENERIC_SIGN_IN_ERROR_MESSAGE);
    this.focusAfterRender(this.submitButton);
  }

  private lockAfterRateLimit(seconds: number): void {
    const minutes = Math.max(1, Math.ceil(seconds / 60));
    this.showError(rateLimitedMessage(minutes));

    clearTimeout(this.retryTimer);
    this.retryLocked.set(true);
    this.retryTimer = setTimeout(() => this.retryLocked.set(false), seconds * 1000);
  }

  private showError(text: string): void {
    this.pageMessage.set({ severity: 'error', text });
  }

  private validateForm(): SignInFieldErrors {
    const errors: SignInFieldErrors = {};
    for (const field of ['email', 'password'] as const) {
      const message = this.validateField(field);
      if (message !== null) {
        errors[field] = message;
      }
    }
    return errors;
  }

  private validateField(field: SignInField): string | null {
    const { email, password } = this.form.getRawValue();
    return field === 'email' ? validateEmail(email) : validatePassword(password);
  }

  private focusFirstInvalid(errors: SignInFieldErrors): void {
    this.focusAfterRender(errors.email !== undefined ? this.emailInput : this.passwordInput);
  }

  /** Focuses after the next render so `disabled`/`readonly` are already updated. */
  private focusAfterRender(target: () => ElementRef<HTMLElement>): void {
    afterNextRender(() => target().nativeElement.focus(), { injector: this.injector });
  }
}
