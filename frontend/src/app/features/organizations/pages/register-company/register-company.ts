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
import { Router } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { CompanyProfileSection } from '../../components/company-profile-section/company-profile-section';
import { DocumentNumberingSection } from '../../components/document-numbering-section/document-numbering-section';
import { ErrorSummary, FieldErrorLink } from '../../components/error-summary/error-summary';
import { FirstBranchSection } from '../../components/first-branch-section/first-branch-section';
import { OwnerAccountSection } from '../../components/owner-account-section/owner-account-section';
import { TaxesCurrencySection } from '../../components/taxes-currency-section/taxes-currency-section';
import { SUPPORTED_COUNTRY_CODES } from '../../data/supported-countries';
import { SUPPORTED_CURRENCY_CODES } from '../../data/supported-currencies';
import {
  browserTimezone,
  countryOptions,
  currencyOptions,
  timezoneOptions,
} from '../../data/display-names';
import {
  BusinessHoursControls,
  BusinessHoursDayControls,
  FieldErrors,
  FieldKey,
  RegisterCompanyFormControls,
  SimpleFieldKey,
  WEEKDAYS,
  Weekday,
  buildRegistrationRequest,
} from '../../models/organization-registration.model';
import { OrganizationRegistrationService } from '../../services/organization-registration.service';
import {
  FIELD_LABELS,
  SIMPLE_FIELD_KEYS,
  businessHoursEndKey,
  businessHoursStartKey,
  fieldControlId,
  mapServerFieldErrors,
  validateBusinessHoursEnd,
  validateBusinessHoursStart,
  validateField,
} from './register-company.validators';

/** Retry lock after a `429` without a usable `Retry-After`. */
const DEFAULT_RETRY_AFTER_SECONDS = 60;

const BUSINESS_HOURS_FIELD_PATTERN = /^branch\.businessHours\.(\w+)\.(start|end)$/;

@Component({
  selector: 'app-register-company',
  imports: [
    ReactiveFormsModule,
    ButtonDirective,
    SpinnerIcon,
    CompanyProfileSection,
    FirstBranchSection,
    TaxesCurrencySection,
    DocumentNumberingSection,
    OwnerAccountSection,
    ErrorSummary,
  ],
  templateUrl: './register-company.html',
  styleUrl: './register-company.scss',
})
export class RegisterCompany {
  private readonly registrationService = inject(OrganizationRegistrationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  private submitAttempted = false;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly timezoneOptionsList = timezoneOptions();
  readonly countryOptionsList = countryOptions(SUPPORTED_COUNTRY_CODES);
  readonly currencyOptionsList = currencyOptions(SUPPORTED_CURRENCY_CODES);

  readonly form = buildRegisterCompanyForm();

  readonly fieldErrors = signal<FieldErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly retryLocked = signal(false);
  readonly branchTimezoneDirty = signal(false);
  readonly submitDisabled = computed(() => this.submitting() || this.retryLocked());

  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    (Object.entries(this.fieldErrors()) as [FieldKey, string][])
      .filter(([field]) => field !== 'branch.businessHours')
      .map(([field, message]) => ({
        fieldId: fieldControlId(field),
        label: FIELD_LABELS[field],
        message,
      })),
  );

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.retryTimer));

    this.form.controls.organization.controls.timezone.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        if (!this.branchTimezoneDirty()) {
          this.form.controls.branch.controls.timezone.setValue(value, { emitEvent: false });
        }
      });
  }

  onBranchTimezoneChanged(): void {
    this.branchTimezoneDirty.set(true);
  }

  onFieldBlur(field: FieldKey): void {
    if (!this.submitAttempted) {
      return;
    }
    this.setFieldError(field, this.computeFieldError(field));
  }

  submit(): void {
    if (this.submitDisabled()) {
      return;
    }

    this.submitAttempted = true;
    this.pageMessage.set(null);

    const errors = this.validateForm();
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusFirstInvalid();
      return;
    }

    this.submitting.set(true);
    const request = buildRegistrationRequest(this.form.getRawValue());
    this.registrationService
      .register(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.handleRegistered(),
        error: (error: unknown) => this.handleRegistrationError(error),
      });
  }

  private handleRegistered(): void {
    // The submitting state stays until the navigation replaces this page.
    this.router.navigateByUrl('/auth/sign-in?registered=true', { replaceUrl: true }).then(
      (navigated) => {
        if (!navigated) {
          this.submitting.set(false);
        }
      },
      () => this.submitting.set(false),
    );
  }

  private handleRegistrationError(error: unknown): void {
    this.submitting.set(false);
    this.form.controls.owner.controls.password.setValue('');
    this.form.controls.confirmPassword.setValue('');

    const apiError: ApiError | null = isApiError(error) ? error : null;

    switch (apiError?.kind) {
      case 'validation': {
        const mapped = mapServerFieldErrors(apiError.fieldErrors);
        this.fieldErrors.set(mapped);
        if (Object.keys(mapped).length > 0) {
          this.pageMessage.set(apiError.message);
          this.focusFirstInvalid();
        } else {
          this.pageMessage.set(apiError.message);
          this.focusSummary();
        }
        return;
      }
      case 'conflict': {
        const mapped = mapServerFieldErrors(apiError.fieldErrors);
        this.fieldErrors.set(mapped);
        this.pageMessage.set(null);
        this.focusFirstInvalid();
        return;
      }
      case 'rate-limited':
        this.fieldErrors.set({});
        this.pageMessage.set(apiError.message);
        this.lockAfterRateLimit(apiError.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS);
        this.focusSummary();
        return;
      case 'bad-request':
        this.fieldErrors.set({});
        this.pageMessage.set(apiError.message);
        this.focusSummary();
        return;
    }

    this.fieldErrors.set({});
    this.pageMessage.set(apiError?.message ?? 'An unexpected error occurred. Please try again.');
    this.focusSummary();
  }

  private lockAfterRateLimit(seconds: number): void {
    clearTimeout(this.retryTimer);
    this.retryLocked.set(true);
    this.retryTimer = setTimeout(() => this.retryLocked.set(false), seconds * 1000);
  }

  private validateForm(): FieldErrors {
    const errors: FieldErrors = {};
    const values = this.form.getRawValue();

    for (const field of SIMPLE_FIELD_KEYS) {
      const message = validateField(field, values);
      if (message !== null) {
        errors[field] = message;
      }
    }

    for (const day of WEEKDAYS) {
      const dayValue = values.branch.businessHours[day];
      if (!dayValue.open) {
        continue;
      }
      const startMessage = validateBusinessHoursStart(dayValue.start);
      if (startMessage !== null) {
        errors[businessHoursStartKey(day)] = startMessage;
      }
      const endMessage = validateBusinessHoursEnd(dayValue.start, dayValue.end);
      if (endMessage !== null) {
        errors[businessHoursEndKey(day)] = endMessage;
      }
    }

    return errors;
  }

  private computeFieldError(field: FieldKey): string | null {
    const businessHoursMatch = BUSINESS_HOURS_FIELD_PATTERN.exec(field);
    if (businessHoursMatch) {
      const day = businessHoursMatch[1] as Weekday;
      const part = businessHoursMatch[2] as 'start' | 'end';
      return this.validateBusinessHoursDay(day, part);
    }
    if (field === 'branch.businessHours') {
      return null;
    }
    return validateField(field as SimpleFieldKey, this.form.getRawValue());
  }

  private validateBusinessHoursDay(day: Weekday, part: 'start' | 'end'): string | null {
    const dayGroup: FormGroup<BusinessHoursDayControls> =
      this.form.controls.branch.controls.businessHours.controls[day];
    if (!dayGroup.controls.open.value) {
      return null;
    }
    return part === 'start'
      ? validateBusinessHoursStart(dayGroup.controls.start.value)
      : validateBusinessHoursEnd(dayGroup.controls.start.value, dayGroup.controls.end.value);
  }

  private setFieldError(field: FieldKey, message: string | null): void {
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

  private focusFirstInvalid(): void {
    afterNextRender(
      () => {
        const firstInvalid = this.hostElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        if (firstInvalid) {
          firstInvalid.focus();
        } else {
          this.focusSummaryNow();
        }
      },
      { injector: this.injector },
    );
  }

  private focusSummary(): void {
    afterNextRender(() => this.focusSummaryNow(), { injector: this.injector });
  }

  private focusSummaryNow(): void {
    this.hostElement.querySelector<HTMLElement>('#register-company-error-summary')?.focus();
  }
}

function buildRegisterCompanyForm(): FormGroup<RegisterCompanyFormControls> {
  const companyTimezone = browserTimezone();

  return new FormGroup({
    organization: new FormGroup({
      name: new FormControl('', { nonNullable: true }),
      legalName: new FormControl('', { nonNullable: true }),
      taxId: new FormControl('', { nonNullable: true }),
      email: new FormControl('', { nonNullable: true }),
      phone: new FormControl('', { nonNullable: true }),
      timezone: new FormControl(companyTimezone, { nonNullable: true }),
      currency: new FormControl('USD', { nonNullable: true }),
      defaultTaxRate: new FormControl<number | null>(0),
      quotePrefix: new FormControl('Q', { nonNullable: true }),
      workOrderPrefix: new FormControl('WO', { nonNullable: true }),
      invoicePrefix: new FormControl('INV', { nonNullable: true }),
      nextInvoiceNumber: new FormControl<number | null>(1),
    }),
    branch: new FormGroup({
      name: new FormControl('', { nonNullable: true }),
      code: new FormControl('', { nonNullable: true }),
      phone: new FormControl('', { nonNullable: true }),
      email: new FormControl('', { nonNullable: true }),
      timezone: new FormControl(companyTimezone, { nonNullable: true }),
      addressLine1: new FormControl('', { nonNullable: true }),
      city: new FormControl('', { nonNullable: true }),
      stateRegion: new FormControl('', { nonNullable: true }),
      postalCode: new FormControl('', { nonNullable: true }),
      countryCode: new FormControl('', { nonNullable: true }),
      businessHours: new FormGroup<BusinessHoursControls>({
        monday: businessHoursDayGroup(true, '08:00', '17:00'),
        tuesday: businessHoursDayGroup(true, '08:00', '17:00'),
        wednesday: businessHoursDayGroup(true, '08:00', '17:00'),
        thursday: businessHoursDayGroup(true, '08:00', '17:00'),
        friday: businessHoursDayGroup(true, '08:00', '17:00'),
        saturday: businessHoursDayGroup(true, '09:00', '13:00'),
        sunday: businessHoursDayGroup(false, '09:00', '17:00'),
      }),
    }),
    owner: new FormGroup({
      firstName: new FormControl('', { nonNullable: true }),
      lastName: new FormControl('', { nonNullable: true }),
      email: new FormControl('', { nonNullable: true }),
      phone: new FormControl('', { nonNullable: true }),
      password: new FormControl('', { nonNullable: true }),
    }),
    confirmPassword: new FormControl('', { nonNullable: true }),
  });
}

function businessHoursDayGroup(
  open: boolean,
  start: string,
  end: string,
): FormGroup<BusinessHoursDayControls> {
  return new FormGroup({
    open: new FormControl(open, { nonNullable: true }),
    start: new FormControl(start, { nonNullable: true }),
    end: new FormControl(end, { nonNullable: true }),
  });
}
