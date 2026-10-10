import { Location } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputPassword } from 'primeng/inputpassword';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { isApiError } from '../../../../core/models/api-error.model';
import { FALLBACK_FIELD_MESSAGE } from '../../../authentication/pages/sign-in/sign-in.messages';
import { rateLimitedMessage } from '../../../authentication/pages/invitation/invitation.messages';
import {
  isTokenOnly,
  validateConfirmPassword,
  validateExistingPassword,
  validateNewPassword,
} from '../../../authentication/pages/invitation/invitation.validators';
import {
  PortalInvitationDetails,
  PortalInvitationService,
} from '../../services/portal-invitation.service';
import { PortalInvitationTokenService } from '../../services/portal-invitation-token.service';

export const ACTIVATE_PATH = '/portal/activate';
export const UNAVAILABLE_MESSAGE =
  "This invitation isn't available. Ask the company to send a new one.";
export const WRONG_PASSWORD_MESSAGE = 'The password is incorrect.';
export const INELIGIBLE_MESSAGE = "This account can't be linked. Contact the company.";
export const ACTIVATE_ERROR_MESSAGE = "We couldn't activate your access. Try again.";
export const LOAD_ERROR_MESSAGE = "We couldn't load this invitation. Try again.";
export const LAST_NAME_REQUIRED = 'Enter your last name.';
export const LAST_NAME_TOO_LONG = 'Last name must be 100 characters or fewer.';
const LAST_NAME_MAX = 100;
const DEFAULT_RETRY_AFTER_SECONDS = 60;

type ActivateState = 'loading' | 'unavailable' | 'load-error' | 'ready';
type ActivateField = 'lastName' | 'password' | 'confirmPassword';

/** `lastName` rule of BR-13: 1–100 characters after trim. */
export function validateLastName(value: string): string | null {
  const length = value.trim().length;
  if (length === 0) {
    return LAST_NAME_REQUIRED;
  }
  return length > LAST_NAME_MAX ? LAST_NAME_TOO_LONG : null;
}

/**
 * Portal activation (BR-12, BR-13). The fragment token is captured into tab memory and the URL is
 * replaced before any request; the token travels only in POST bodies.
 */
@Component({
  selector: 'app-portal-activate',
  imports: [
    ReactiveFormsModule,
    ButtonDirective,
    InputPassword,
    InputText,
    Message,
    RouterLink,
    Skeleton,
    SpinnerIcon,
  ],
  templateUrl: './activate.html',
  styleUrl: '../../portal-auth.scss',
})
export class PortalActivate {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);
  private readonly invitations = inject(PortalInvitationService);
  private readonly tokens = inject(PortalInvitationTokenService);
  private retryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly form = new FormGroup({
    lastName: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true }),
    confirmPassword: new FormControl('', { nonNullable: true }),
  });

  readonly state = signal<ActivateState>('loading');
  readonly detail = signal<PortalInvitationDetails | null>(null);
  readonly fieldErrors = signal<Partial<Record<ActivateField, string>>>({});
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly retryLocked = signal(false);
  readonly submitDisabled = computed(() => this.submitting() || this.retryLocked());
  readonly existing = computed(() => this.detail()?.accountExists === true);
  readonly lastNameRequired = computed(
    () => !this.existing() && this.detail()?.lastNameRequired === true,
  );

  readonly unavailableMessage = UNAVAILABLE_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.retryTimer));
    // BR-12: capture first, replace the URL before any request; `?token=` is ignored.
    const snapshot = this.route.snapshot;
    this.tokens.captureFromFragment(snapshot.fragment);
    if (snapshot.fragment !== null || snapshot.queryParamMap.keys.length > 0) {
      this.location.replaceState(ACTIVATE_PATH);
    }
    this.load();
  }

  retry(): void {
    this.load();
  }

  /** Leaving the page through a route out clears the stored token. */
  leave(): void {
    this.tokens.clear();
  }

  submit(): void {
    if (this.submitDisabled() || this.state() !== 'ready') {
      return;
    }
    this.pageMessage.set(null);
    const errors = this.validate();
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }
    const token = this.tokens.read();
    if (token === null) {
      this.showUnavailable();
      return;
    }

    const values = this.form.getRawValue();
    this.submitting.set(true);
    const request = this.existing()
      ? this.invitations.acceptExisting(token, values.password)
      : this.invitations.accept(
          token,
          values.password,
          this.lastNameRequired() ? values.lastName.trim() : null,
        );
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.tokens.clear();
        // The submitting state stays until the navigation replaces this page.
        this.router.navigateByUrl('/portal', { replaceUrl: true }).then(
          (navigated) => (navigated ? undefined : this.submitting.set(false)),
          () => this.submitting.set(false),
        );
      },
      error: (error: unknown) => this.failed(error),
    });
  }

  private load(): void {
    const token = this.tokens.read();
    if (token === null) {
      this.showUnavailable();
      return;
    }
    this.state.set('loading');
    this.invitations
      .validate(token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.detail.set(detail);
          this.state.set('ready');
        },
        error: (error: unknown) => {
          const apiError = isApiError(error) ? error : null;
          if (
            apiError?.status === 410 ||
            (apiError?.kind === 'validation' && isTokenOnly(apiError.fieldErrors))
          ) {
            this.showUnavailable();
          } else {
            this.state.set('load-error');
          }
        },
      });
  }

  private failed(error: unknown): void {
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
        const errors: Partial<Record<ActivateField, string>> = {};
        if (apiError.fieldErrors['lastName'] !== undefined) {
          errors.lastName =
            this.form.controls.lastName.value.trim().length > LAST_NAME_MAX
              ? LAST_NAME_TOO_LONG
              : LAST_NAME_REQUIRED;
        }
        if (apiError.fieldErrors['password'] !== undefined) {
          errors.password = FALLBACK_FIELD_MESSAGE;
        }
        if (Object.keys(errors).length > 0) {
          this.fieldErrors.set(errors);
          return;
        }
        break;
      }
      case 'unauthorized':
        this.form.controls.password.setValue('');
        this.pageMessage.set(WRONG_PASSWORD_MESSAGE);
        return;
      case 'conflict':
        this.pageMessage.set(INELIGIBLE_MESSAGE);
        return;
      case 'rate-limited': {
        const seconds = apiError.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS;
        this.pageMessage.set(rateLimitedMessage(Math.max(1, Math.ceil(seconds / 60))));
        clearTimeout(this.retryTimer);
        this.retryLocked.set(true);
        this.retryTimer = setTimeout(() => this.retryLocked.set(false), seconds * 1000);
        return;
      }
    }
    this.pageMessage.set(ACTIVATE_ERROR_MESSAGE);
  }

  private showUnavailable(): void {
    this.tokens.clear();
    this.detail.set(null);
    this.form.reset();
    this.state.set('unavailable');
  }

  private validate(): Partial<Record<ActivateField, string>> {
    const values = this.form.getRawValue();
    const email = this.detail()?.email ?? '';
    const errors: Partial<Record<ActivateField, string>> = {};
    if (this.existing()) {
      const message = validateExistingPassword(values.password);
      if (message !== null) {
        errors.password = message;
      }
      return errors;
    }
    if (this.lastNameRequired()) {
      const message = validateLastName(values.lastName);
      if (message !== null) {
        errors.lastName = message;
      }
    }
    const password = validateNewPassword(values.password, email);
    if (password !== null) {
      errors.password = password;
    }
    const confirm = validateConfirmPassword(values.confirmPassword, values.password);
    if (confirm !== null) {
      errors.confirmPassword = confirm;
    }
    return errors;
  }
}
