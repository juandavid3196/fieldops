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
import { switchMap, tap } from 'rxjs';
import { ButtonDirective } from 'primeng/button';
import { Checkbox, CheckboxChangeEvent } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputPassword } from 'primeng/inputpassword';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { InvitationBrandPanel } from '../../components/invitation-brand-panel/invitation-brand-panel';
import { InvitationDetails } from '../../models/invitation.model';
import { InvitationTokenService } from '../../services/invitation-token.service';
import { InvitationService } from '../../services/invitation.service';
import {
  ACCOUNT_EXISTS_MESSAGE,
  FIRST_NAME_REQUIRED_MESSAGE,
  GENERIC_ACCEPT_ERROR_MESSAGE,
  INVALID_CREDENTIALS_MESSAGE,
  LAST_NAME_REQUIRED_MESSAGE,
  LOAD_ERROR_MESSAGE,
  UNAVAILABLE_BODY,
  UNAVAILABLE_NOTE,
  UNAVAILABLE_TITLE,
  cannotAcceptMessage,
  expiryNote,
  rateLimitedMessage,
} from './invitation.messages';
import {
  InvitationField,
  InvitationFieldErrors,
  isTokenOnly,
  mapServerFieldErrors,
  passwordRequirements,
  validateConfirmPassword,
  validateExistingPassword,
  validateName,
  validateNewPassword,
} from './invitation.validators';

type InvitationState = 'loading' | 'unavailable' | 'load-error' | 'ready';
type InvitationMode = 'new-account' | 'existing-account';

interface PageMessage {
  readonly text: string;
  /** `409 account_exists`: offer the switch to the existing-account mode. */
  readonly offerSignIn: boolean;
}

/** Retry lock after a `429` without a usable `Retry-After` (BR-17). */
const DEFAULT_RETRY_AFTER_SECONDS = 60;
const INVITATION_PATH = '/auth/invitation';
const MS_PER_DAY = 86_400_000;

const FIELD_IDS: Readonly<Record<InvitationField, string>> = {
  firstName: 'first-name',
  lastName: 'last-name',
  password: 'password',
  confirmPassword: 'confirm-password',
};

@Component({
  selector: 'app-invitation',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonDirective,
    Checkbox,
    InputPassword,
    InputText,
    Message,
    Skeleton,
    SpinnerIcon,
    InvitationBrandPanel,
  ],
  templateUrl: './invitation.html',
  styleUrl: './invitation.scss',
})
export class Invitation {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly sessionService = inject(SessionService);
  private readonly invitations = inject(InvitationService);
  private readonly tokens = inject(InvitationTokenService);

  private submitAttempted = false;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly form = new FormGroup({
    firstName: new FormControl('', { nonNullable: true }),
    lastName: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true }),
    confirmPassword: new FormControl('', { nonNullable: true }),
    showPassword: new FormControl(false, { nonNullable: true }),
  });

  readonly state = signal<InvitationState>('loading');
  readonly mode = signal<InvitationMode>('new-account');
  readonly detail = signal<InvitationDetails | null>(null);
  private readonly loadedAt = signal(Date.now());
  readonly loadErrorMessage = signal(LOAD_ERROR_MESSAGE);
  readonly pageMessage = signal<PageMessage | null>(null);
  readonly fieldErrors = signal<InvitationFieldErrors>({});
  readonly submitting = signal(false);
  readonly retryLocked = signal(false);
  readonly passwordMasked = signal(true);
  readonly submitDisabled = computed(() => this.submitting() || this.retryLocked());

  readonly unavailableTitle = UNAVAILABLE_TITLE;
  readonly unavailableBody = UNAVAILABLE_BODY;
  readonly unavailableNote = UNAVAILABLE_NOTE;

  private readonly passwordValue = toSignal(this.form.controls.password.valueChanges, {
    initialValue: '',
  });
  readonly requirements = computed(() =>
    passwordRequirements(this.passwordValue(), this.detail()?.email ?? ''),
  );
  readonly branchAccess = computed(() => {
    const detail = this.detail();
    if (detail === null) {
      return '';
    }
    if (detail.isAllBranches) {
      return 'All branches';
    }
    return detail.branches.length > 0
      ? detail.branches.map((branch) => branch.name).join(', ')
      : 'No active branches';
  });
  readonly expiryText = computed(() => {
    const expiresAt = Date.parse(this.detail()?.expiresAt ?? '');
    const days = Number.isNaN(expiresAt)
      ? 1
      : Math.ceil((expiresAt - this.loadedAt()) / MS_PER_DAY);
    return expiryNote(Math.max(1, days));
  });

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.retryTimer));

    // BR-16: capture first, replace the URL before any request; `?token=` is ignored.
    const snapshot = this.route.snapshot;
    this.tokens.captureFromFragment(snapshot.fragment);
    if (snapshot.fragment !== null || snapshot.queryParamMap.keys.length > 0) {
      this.location.replaceState(INVITATION_PATH);
    }

    this.load();
  }

  retry(): void {
    if (!this.retryLocked()) {
      this.load();
    }
  }

  /** Leaving the page by any route out clears the stored token (BR-16). */
  leave(): void {
    this.tokens.clear();
  }

  togglePassword(event: CheckboxChangeEvent): void {
    this.passwordMasked.set(!event.checked);
  }

  switchMode(mode: InvitationMode): void {
    this.mode.set(mode);
    this.submitAttempted = false;
    this.fieldErrors.set({});
    this.pageMessage.set(null);
    this.form.patchValue({ password: '', confirmPassword: '' });
    this.focusHeading();
  }

  validateOnBlur(field: InvitationField): void {
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

    const errors = this.validateForm();
    this.fieldErrors.set(errors);
    const firstInvalid = (['firstName', 'lastName', 'password', 'confirmPassword'] as const).find(
      (field) => errors[field] !== undefined,
    );
    if (firstInvalid !== undefined) {
      this.focusAfterRender(firstInvalid);
      return;
    }

    const token = this.tokens.read();
    const detail = this.detail();
    if (token === null || detail === null) {
      this.showUnavailable();
      return;
    }

    const values = this.form.getRawValue();
    this.submitting.set(true);

    if (this.mode() === 'new-account') {
      this.invitations
        .accept({
          token,
          firstName: values.firstName.trim(),
          lastName: values.lastName.trim(),
          password: values.password,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: () => this.handleAccepted(),
          error: (error: unknown) => this.handleActionError(error, false),
        });
      return;
    }

    let signedIn = false;
    this.sessionService
      .signIn({ email: detail.email, password: values.password, rememberMe: false })
      .pipe(
        tap(() => (signedIn = true)),
        switchMap(() => this.invitations.acceptExisting(token)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => this.handleAccepted(),
        error: (error: unknown) => this.handleActionError(error, signedIn),
      });
  }

  private load(): void {
    const token = this.tokens.read();
    if (token === null) {
      this.showUnavailable();
      return;
    }

    this.state.set('loading');
    this.pageMessage.set(null);
    this.invitations
      .validate(token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.detail.set(detail);
          this.loadedAt.set(Date.now());
          this.form.patchValue({ firstName: detail.firstName, lastName: detail.lastName });
          this.state.set('ready');
          this.focusHeading();
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
      const seconds = apiError.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS;
      this.loadErrorMessage.set(rateLimitedMessage(Math.max(1, Math.ceil(seconds / 60))));
      this.lockAfterRateLimit(seconds);
    } else {
      this.loadErrorMessage.set(LOAD_ERROR_MESSAGE);
    }
    this.state.set('load-error');
    this.focusHeading();
  }

  private handleAccepted(): void {
    this.tokens.clear();
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

  private handleActionError(error: unknown, afterSignIn: boolean): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;
    const newAccount = this.mode() === 'new-account';

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
        const errors: InvitationFieldErrors = newAccount
          ? mapServerFieldErrors(apiError.fieldErrors)
          : {};
        const firstInvalid = (['firstName', 'lastName', 'password'] as const).find(
          (field) => errors[field] !== undefined,
        );
        if (firstInvalid !== undefined) {
          this.fieldErrors.set(errors);
          this.focusAfterRender(firstInvalid);
          return;
        }
        break;
      }
      case 'unauthorized':
        if (!newAccount && !afterSignIn) {
          this.form.controls.password.setValue('');
          this.showError(INVALID_CREDENTIALS_MESSAGE);
          this.focusAfterRender('password');
          return;
        }
        break;
      case 'conflict':
        if (newAccount && apiError.code === 'account_exists') {
          this.pageMessage.set({ text: ACCOUNT_EXISTS_MESSAGE, offerSignIn: true });
        } else {
          this.showError(
            cannotAcceptMessage(this.detail()?.organizationName ?? 'your organization'),
          );
        }
        return;
      case 'rate-limited': {
        const seconds = apiError.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS;
        this.showError(rateLimitedMessage(Math.max(1, Math.ceil(seconds / 60))));
        this.lockAfterRateLimit(seconds);
        return;
      }
    }

    this.showError(GENERIC_ACCEPT_ERROR_MESSAGE);
  }

  private showUnavailable(): void {
    this.tokens.clear();
    this.detail.set(null);
    this.form.reset();
    this.pageMessage.set(null);
    this.state.set('unavailable');
    this.focusHeading();
  }

  private showError(text: string): void {
    this.pageMessage.set({ text, offerSignIn: false });
  }

  private lockAfterRateLimit(seconds: number): void {
    clearTimeout(this.retryTimer);
    this.retryLocked.set(true);
    this.retryTimer = setTimeout(() => this.retryLocked.set(false), seconds * 1000);
  }

  private validateForm(): InvitationFieldErrors {
    const errors: InvitationFieldErrors = {};
    const fields: readonly InvitationField[] =
      this.mode() === 'new-account'
        ? ['firstName', 'lastName', 'password', 'confirmPassword']
        : ['password'];
    for (const field of fields) {
      const message = this.validateField(field);
      if (message !== null) {
        errors[field] = message;
      }
    }
    return errors;
  }

  private validateField(field: InvitationField): string | null {
    const values = this.form.getRawValue();
    const email = this.detail()?.email ?? '';
    switch (field) {
      case 'firstName':
        return validateName(values.firstName, FIRST_NAME_REQUIRED_MESSAGE);
      case 'lastName':
        return validateName(values.lastName, LAST_NAME_REQUIRED_MESSAGE);
      case 'password':
        return this.mode() === 'new-account'
          ? validateNewPassword(values.password, email)
          : validateExistingPassword(values.password);
      case 'confirmPassword':
        return validateConfirmPassword(values.confirmPassword, values.password);
    }
  }

  private focusHeading(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('h1')?.focus(), {
      injector: this.injector,
    });
  }

  private focusAfterRender(field: InvitationField): void {
    afterNextRender(
      () => {
        this.host.nativeElement.querySelector<HTMLElement>(`#${FIELD_IDS[field]}`)?.focus();
      },
      { injector: this.injector },
    );
  }
}
