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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';

import { isApiError } from '../../../../core/models/api-error.model';
import { InvitationBrandPanel } from '../../components/invitation-brand-panel/invitation-brand-panel';
import { PasswordResetService } from '../../services/password-reset.service';
import { rateLimitedMessage } from '../invitation/invitation.messages';
import { mapServerFieldErrors, normalizeEmail, validateEmail } from '../sign-in/sign-in.validators';
import { REQUEST_ERROR_MESSAGE } from './forgot-password.messages';

type ForgotPasswordState = 'form' | 'sent';

/** Retry lock after a `429` without a usable `Retry-After` (BR-19). */
const DEFAULT_RETRY_AFTER_SECONDS = 60;

@Component({
  selector: 'app-forgot-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonDirective,
    InputText,
    Message,
    SpinnerIcon,
    InvitationBrandPanel,
  ],
  templateUrl: './forgot-password.html',
  // Shares the invitation card layout (same authentication split screen).
  styleUrl: '../invitation/invitation.scss',
})
export class ForgotPassword {
  private readonly passwordResets = inject(PasswordResetService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  private submitAttempted = false;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
  });

  readonly state = signal<ForgotPasswordState>('form');
  readonly submittedEmail = signal('');
  readonly emailError = signal<string | null>(null);
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly retryLocked = signal(false);
  readonly submitDisabled = computed(() => this.submitting() || this.retryLocked());

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.retryTimer));
  }

  validateOnBlur(): void {
    if (this.submitAttempted) {
      this.emailError.set(validateEmail(this.form.controls.email.value));
    }
  }

  submit(): void {
    if (this.submitDisabled()) {
      return;
    }

    this.submitAttempted = true;
    this.pageMessage.set(null);

    const error = validateEmail(this.form.controls.email.value);
    this.emailError.set(error);
    if (error !== null) {
      this.focus('#email');
      return;
    }

    const email = normalizeEmail(this.form.controls.email.value);
    this.submitting.set(true);
    this.passwordResets
      .request(email)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.submittedEmail.set(email);
          this.state.set('sent');
          this.focus('h1');
        },
        error: (failure: unknown) => this.handleError(failure),
      });
  }

  /** Returns to the form with the typed email kept and focused. */
  useDifferentEmail(): void {
    this.state.set('form');
    this.focus('#email');
  }

  private handleError(failure: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(failure) ? failure : null;

    if (apiError?.kind === 'validation') {
      const message = mapServerFieldErrors(apiError.fieldErrors).email;
      if (message !== undefined) {
        this.emailError.set(message);
        this.focus('#email');
        return;
      }
    } else if (apiError?.kind === 'rate-limited') {
      const seconds = apiError.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS;
      this.pageMessage.set(rateLimitedMessage(Math.max(1, Math.ceil(seconds / 60))));
      clearTimeout(this.retryTimer);
      this.retryLocked.set(true);
      this.retryTimer = setTimeout(() => this.retryLocked.set(false), seconds * 1000);
      this.focus('#email');
      return;
    }

    this.pageMessage.set(REQUEST_ERROR_MESSAGE);
    this.focus('button[type="submit"]');
  }

  /** Focuses after the next render so the target and its readonly state are current. */
  private focus(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
