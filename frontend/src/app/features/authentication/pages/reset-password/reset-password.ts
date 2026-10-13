import { Location } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Checkbox, CheckboxChangeEvent } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputPassword } from 'primeng/inputpassword';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { isApiError } from '../../../../core/models/api-error.model';
import {
  PASSWORD_EQUALS_EMAIL_MESSAGE,
  PASSWORD_LENGTH_MESSAGE,
  REQUIRED_MESSAGE,
} from '../../../organizations/pages/register-company/register-company.messages';
import { InvitationBrandPanel } from '../../components/invitation-brand-panel/invitation-brand-panel';
import { PasswordResetTokenService } from '../../services/password-reset-token.service';
import { PasswordResetService } from '../../services/password-reset.service';
import { rateLimitedMessage } from '../invitation/invitation.messages';
import {
  isTokenOnly,
  passwordRequirements,
  validateConfirmPassword,
  validateNewPassword,
} from '../invitation/invitation.validators';
import { FALLBACK_FIELD_MESSAGE } from '../sign-in/sign-in.messages';
import {
  LOAD_ERROR_MESSAGE,
  RESET_ERROR_MESSAGE,
  UNAVAILABLE_BODY,
  UNAVAILABLE_TITLE,
} from './reset-password.messages';

type ResetPasswordState = 'loading' | 'unavailable' | 'load-error' | 'ready' | 'done';
type ResetField = 'password' | 'confirmPassword';
type ResetFieldErrors = Partial<Record<ResetField, string>>;

/** Retry lock after a `429` without a usable `Retry-After` (BR-19). */
const DEFAULT_RETRY_AFTER_SECONDS = 60;
const FIELD_IDS: Readonly<Record<ResetField, string>> = {
  password: 'password',
  confirmPassword: 'confirm-password',
};
const SERVER_PASSWORD_MESSAGES: readonly string[] = [
  REQUIRED_MESSAGE,
  PASSWORD_LENGTH_MESSAGE,
  PASSWORD_EQUALS_EMAIL_MESSAGE,
];

/** Server `400` field errors: only `password`; any other key or message gets the fallback. */
function mapServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): ResetFieldErrors {
  if (Object.keys(fieldErrors).length === 0) {
    return {};
  }
  const message = fieldErrors['password']?.[0];
  return {
    password:
      message !== undefined && SERVER_PASSWORD_MESSAGES.includes(message)
        ? message
        : FALLBACK_FIELD_MESSAGE,
  };
}

@Component({
  selector: 'app-reset-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonDirective,
    Checkbox,
    InputPassword,
    Message,
    Skeleton,
    SpinnerIcon,
    InvitationBrandPanel,
  ],
  templateUrl: './reset-password.html',
  // Shares the invitation card layout (same authentication split screen).
  styleUrl: '../invitation/invitation.scss',
})
export class ResetPassword {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly passwordResets = inject(PasswordResetService);
  /** Route data `portal: true` serves the customer portal: its paths and sign-in link. */
  private readonly portal = this.route.snapshot.data['portal'] === true;
  private readonly resetPath = this.portal ? '/portal/reset-password' : '/auth/reset-password';
  protected readonly signInPath = this.portal ? '/portal/sign-in' : '/auth/sign-in';
  protected readonly forgotPath = this.portal ? '/portal/forgot-password' : '/auth/forgot-password';
  private readonly tokens = inject(PasswordResetTokenService);

  private submitAttempted = false;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly form = new FormGroup({
    password: new FormControl('', { nonNullable: true }),
    confirmPassword: new FormControl('', { nonNullable: true }),
    showPassword: new FormControl(false, { nonNullable: true }),
  });

  readonly state = signal<ResetPasswordState>('loading');
  /** Account email from validate; kept in memory only. */
  readonly email = signal('');
  readonly loadErrorMessage = signal(LOAD_ERROR_MESSAGE);
  readonly pageMessage = signal<string | null>(null);
  readonly fieldErrors = signal<ResetFieldErrors>({});
  readonly submitting = signal(false);
  readonly retryLocked = signal(false);
  readonly passwordMasked = signal(true);
  readonly submitDisabled = computed(() => this.submitting() || this.retryLocked());

  readonly unavailableTitle = UNAVAILABLE_TITLE;
  readonly unavailableBody = UNAVAILABLE_BODY;

  private readonly passwordValue = toSignal(this.form.controls.password.valueChanges, {
    initialValue: '',
  });
  readonly requirements = computed(() => passwordRequirements(this.passwordValue(), this.email()));

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.retryTimer));

    // BR-20: capture first, replace the URL before any request; `?token=` is ignored.
    const snapshot = this.route.snapshot;
    this.tokens.captureFromFragment(snapshot.fragment);
    if (snapshot.fragment !== null || snapshot.queryParamMap.keys.length > 0) {
      this.location.replaceState(this.resetPath);
    }

    this.load();
  }

  retry(): void {
    if (!this.retryLocked()) {
      this.load();
    }
  }

  /** Leaving through Back to sign in or Request a new link clears the stored token (BR-20). */
  leave(): void {
    this.tokens.clear();
  }

  continueToSignIn(): void {
    void this.router.navigateByUrl(this.signInPath, { replaceUrl: true });
  }

  togglePassword(event: CheckboxChangeEvent): void {
    this.passwordMasked.set(!event.checked);
  }

  validateOnBlur(field: ResetField): void {
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
    if (this.submitDisabled() || this.state() !== 'ready') {
      return;
    }

    this.submitAttempted = true;
    this.pageMessage.set(null);

    const errors: ResetFieldErrors = {};
    for (const field of ['password', 'confirmPassword'] as const) {
      const message = this.validateField(field);
      if (message !== null) {
        errors[field] = message;
      }
    }
    this.fieldErrors.set(errors);
    const firstInvalid = (['password', 'confirmPassword'] as const).find(
      (field) => errors[field] !== undefined,
    );
    if (firstInvalid !== undefined) {
      this.focus(`#${FIELD_IDS[firstInvalid]}`);
      return;
    }

    const token = this.tokens.read();
    if (token === null) {
      this.showUnavailable();
      return;
    }

    this.submitting.set(true);
    this.passwordResets
      .confirm(token, this.form.controls.password.value)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.handleDone(),
        error: (error: unknown) => this.handleActionError(error),
      });
  }

  private load(): void {
    const token = this.tokens.read();
    if (token === null) {
      this.showUnavailable();
      return;
    }

    this.state.set('loading');
    this.passwordResets
      .validate(token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (details) => {
          this.email.set(details.email);
          this.state.set('ready');
          this.focus('h1');
        },
        error: (error: unknown) => this.handleLoadError(error),
      });
  }

  private handleLoadError(error: unknown): void {
    const apiError = isApiError(error) ? error : null;
    if (
      apiError !== null &&
      (apiError.status === 410 ||
        (apiError.kind === 'validation' && isTokenOnly(apiError.fieldErrors)))
    ) {
      this.showUnavailable();
      return;
    }

    if (apiError?.kind === 'rate-limited') {
      this.loadErrorMessage.set(this.lockAfterRateLimit(apiError.retryAfterSeconds));
    } else {
      this.loadErrorMessage.set(LOAD_ERROR_MESSAGE);
    }
    this.state.set('load-error');
    this.focus('h1');
  }

  private handleDone(): void {
    this.tokens.clear();
    this.submitting.set(false);
    this.email.set('');
    this.form.reset();
    this.fieldErrors.set({});
    this.pageMessage.set(null);
    this.state.set('done');
    this.focus('h1');
  }

  private handleActionError(error: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;

    if (apiError?.status === 410) {
      this.showUnavailable();
      return;
    }

    switch (apiError?.kind) {
      case 'validation': {
        if (isTokenOnly(apiError.fieldErrors)) {
          this.showUnavailable();
          return;
        }
        const errors = mapServerFieldErrors(apiError.fieldErrors);
        if (errors.password !== undefined) {
          this.fieldErrors.set(errors);
          this.focus('#password');
          return;
        }
        break;
      }
      case 'rate-limited':
        this.pageMessage.set(this.lockAfterRateLimit(apiError.retryAfterSeconds));
        return;
    }

    this.pageMessage.set(RESET_ERROR_MESSAGE);
  }

  private showUnavailable(): void {
    this.tokens.clear();
    this.email.set('');
    this.form.reset();
    this.fieldErrors.set({});
    this.pageMessage.set(null);
    this.state.set('unavailable');
    this.focus('h1');
  }

  /** Locks submit/Retry for the period and returns the BR-19 message. */
  private lockAfterRateLimit(retryAfterSeconds: number | undefined): string {
    const seconds = retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS;
    clearTimeout(this.retryTimer);
    this.retryLocked.set(true);
    this.retryTimer = setTimeout(() => this.retryLocked.set(false), seconds * 1000);
    return rateLimitedMessage(Math.max(1, Math.ceil(seconds / 60)));
  }

  private validateField(field: ResetField): string | null {
    const values = this.form.getRawValue();
    return field === 'password'
      ? validateNewPassword(values.password, this.email())
      : validateConfirmPassword(values.confirmPassword, values.password);
  }

  private focus(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
