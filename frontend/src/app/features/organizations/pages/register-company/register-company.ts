import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Observable, map } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { BusinessHoursSection } from '../../components/business-hours-section/business-hours-section';
import { CompanyProfileSection } from '../../components/company-profile-section/company-profile-section';
import { DocumentNumberingSection } from '../../components/document-numbering-section/document-numbering-section';
import { ErrorSummary, FieldErrorLink } from '../../components/error-summary/error-summary';
import { FirstBranchSection } from '../../components/first-branch-section/first-branch-section';
import { OwnerAccountSection } from '../../components/owner-account-section/owner-account-section';
import { ReviewStep } from '../../components/review-step/review-step';
import { TaxesCurrencySection } from '../../components/taxes-currency-section/taxes-currency-section';
import { WizardSidePanel } from '../../components/wizard-side-panel/wizard-side-panel';
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
  FormStep,
  WizardStep,
  copyMondayToWeekdays,
  numberingExamples,
  passwordRequirements,
  stepOfField,
} from './register-company.helpers';
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

const STEP_HEADERS: Readonly<Record<WizardStep, { title: string; description: string }>> = {
  1: {
    title: 'Create your organization',
    description:
      'Tell us about your company and first location. You can change these details later.',
  },
  2: {
    title: 'Business settings',
    description: 'Set your operating hours, currency, taxes, and document numbering.',
  },
  3: {
    title: 'Owner account',
    description: "Create the account you'll use to manage this organization.",
  },
  4: {
    title: 'Review and create',
    description: 'Confirm your details before creating your FieldOps workspace.',
  },
};

type FocusTarget = 'title' | 'invalid' | 'summary';

/**
 * Organization onboarding wizard (`/auth/register-company`): four steps over one reactive form
 * held only in memory (BR-11), validated per step, sent in a single request from Review.
 */
@Component({
  selector: 'app-register-company',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonDirective,
    ConfirmDialog,
    SpinnerIcon,
    ErrorSummary,
    WizardSidePanel,
    CompanyProfileSection,
    FirstBranchSection,
    BusinessHoursSection,
    TaxesCurrencySection,
    DocumentNumberingSection,
    OwnerAccountSection,
    ReviewStep,
  ],
  providers: [ConfirmationService],
  templateUrl: './register-company.html',
  styleUrl: './register-company.scss',
})
export class RegisterCompany {
  private readonly registrationService = inject(OrganizationRegistrationService);
  private readonly router = inject(Router);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  private submitAttempted = false;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly timezoneOptionsList = timezoneOptions();
  readonly countryOptionsList = countryOptions(SUPPORTED_COUNTRY_CODES);
  readonly currencyOptionsList = currencyOptions(SUPPORTED_CURRENCY_CODES);

  readonly form = buildRegisterCompanyForm();
  private readonly initialValue = JSON.stringify(this.form.getRawValue());

  /** Memory-only mirror of the form value for derived, live UI (examples, requirements, review). */
  readonly formValue = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });

  readonly step = signal<WizardStep>(1);
  readonly reached = signal<ReadonlySet<WizardStep>>(new Set<WizardStep>([1]));
  readonly completed = signal<ReadonlySet<WizardStep>>(new Set<WizardStep>());
  readonly fieldErrors = signal<FieldErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly retryLocked = signal(false);
  readonly branchTimezoneDirty = signal(false);
  private readonly registered = signal(false);

  readonly header = computed(() => STEP_HEADERS[this.step()]);
  readonly reviewReached = computed(() => this.reached().has(4));
  readonly primaryLabel = computed(() =>
    this.step() < 4 && this.reviewReached() ? 'Back to review' : 'Continue',
  );
  readonly createDisabled = computed(() => this.submitting() || this.retryLocked());
  readonly changed = computed(() => JSON.stringify(this.formValue()) !== this.initialValue);

  readonly examples = computed(() => numberingExamples(this.formValue().organization));
  readonly requirements = computed(() =>
    passwordRequirements(this.formValue().owner.password, this.formValue().owner.email),
  );

  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    (Object.entries(this.fieldErrors()) as [FieldKey, string][])
      .filter(([field]) => stepOfField(field) === this.step())
      .map(([field, message]) => ({
        fieldId: fieldControlId(field),
        label: FIELD_LABELS[field],
        message,
      })),
  );

  private readonly beforeUnload = (event: BeforeUnloadEvent): void => event.preventDefault();

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.retryTimer));

    this.form.controls.organization.controls.timezone.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        if (!this.branchTimezoneDirty()) {
          this.form.controls.branch.controls.timezone.setValue(value, { emitEvent: false });
        }
      });

    // FR-12: the browser leave prompt exists only while the wizard holds changed data.
    effect((onCleanup) => {
      const target = this.window;
      if (target && this.changed() && !this.registered()) {
        target.addEventListener('beforeunload', this.beforeUnload);
        onCleanup(() => target.removeEventListener('beforeunload', this.beforeUnload));
      }
    });
  }

  /** Consulted by `registerCompanyLeaveGuard` (FR-12, BR-08). */
  canLeave(): boolean | Observable<boolean> {
    if (this.registered() || JSON.stringify(this.form.getRawValue()) === this.initialValue) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm({
        header: 'Discard setup?',
        message: "Your organization hasn't been created. The details you entered will be lost.",
        acceptButtonProps: { label: 'Discard', severity: 'danger' },
        rejectButtonProps: { label: 'Keep editing', severity: 'secondary', outlined: true },
        accept: () => {
          subscriber.next(true);
          subscriber.complete();
        },
        reject: () => {
          subscriber.next(false);
          subscriber.complete();
        },
      });
    });
  }

  onBranchTimezoneChanged(): void {
    this.branchTimezoneDirty.set(true);
  }

  copyMonday(): void {
    const hours = this.form.controls.branch.controls.businessHours;
    hours.patchValue(copyMondayToWeekdays(hours.getRawValue()));
  }

  onFieldBlur(field: FieldKey): void {
    if (!this.submitAttempted) {
      return;
    }
    this.setFieldError(field, this.computeFieldError(field));
  }

  back(): void {
    if (this.submitting() || this.step() === 1) {
      return;
    }
    this.goTo((this.step() - 1) as WizardStep, 'title');
  }

  selectStep(step: WizardStep): void {
    if (this.submitting() || !this.reached().has(step)) {
      return;
    }
    this.goTo(step, 'title');
  }

  edit(step: FormStep): void {
    if (!this.submitting()) {
      this.goTo(step, 'title');
    }
  }

  /** Form submit (Enter or the primary button): Continue / Back to review, or Create. */
  primaryAction(): void {
    if (this.submitting()) {
      return;
    }
    if (this.step() === 4) {
      this.create();
    } else {
      this.continueFromStep(this.step() as FormStep);
    }
  }

  private continueFromStep(step: FormStep): void {
    this.submitAttempted = true;
    const errors = this.validateStep(step);
    this.fieldErrors.update((current) => ({ ...withoutStep(current, step), ...errors }));
    if (Object.keys(errors).length > 0) {
      this.focusAfterRender('invalid');
      return;
    }
    this.completed.update((set) => new Set<WizardStep>(set).add(step));
    this.goTo(this.reviewReached() ? 4 : ((step + 1) as WizardStep), 'title');
  }

  private create(): void {
    if (this.createDisabled()) {
      return;
    }

    this.submitAttempted = true;
    this.pageMessage.set(null);

    const errors: FieldErrors = {
      ...this.validateStep(1),
      ...this.validateStep(2),
      ...this.validateStep(3),
    };
    this.fieldErrors.set(errors);
    const invalid = Object.keys(errors) as FieldKey[];
    if (invalid.length > 0) {
      this.goTo(Math.min(...invalid.map(stepOfField)) as WizardStep, 'invalid');
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
    // The submitting state stays until the navigation replaces this page; success skips FR-12.
    this.registered.set(true);
    this.router.navigateByUrl('/auth/sign-in?registered=true', { replaceUrl: true }).then(
      (navigated) => {
        if (!navigated) {
          this.registered.set(false);
          this.submitting.set(false);
        }
      },
      () => {
        this.registered.set(false);
        this.submitting.set(false);
      },
    );
  }

  /** BR-10: mapped field errors open the earliest step with one; everything else stays on Review. */
  private handleRegistrationError(error: unknown): void {
    this.submitting.set(false);

    const apiError: ApiError | null = isApiError(error) ? error : null;

    if (apiError?.kind === 'validation' || apiError?.kind === 'conflict') {
      const mapped = mapServerFieldErrors(apiError.fieldErrors);
      const keys = Object.keys(mapped) as FieldKey[];
      if (keys.length > 0) {
        this.fieldErrors.set(mapped);
        this.pageMessage.set(null);
        this.goTo(Math.min(...keys.map(stepOfField)) as WizardStep, 'invalid');
        return;
      }
    }

    this.fieldErrors.set({});
    this.pageMessage.set(apiError?.message ?? 'An unexpected error occurred. Please try again.');
    if (apiError?.kind === 'rate-limited') {
      this.lockAfterRateLimit(apiError.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS);
    }
    this.goTo(4, 'summary');
  }

  private lockAfterRateLimit(seconds: number): void {
    clearTimeout(this.retryTimer);
    this.retryLocked.set(true);
    this.retryTimer = setTimeout(() => this.retryLocked.set(false), seconds * 1000);
  }

  private goTo(step: WizardStep, focus: FocusTarget): void {
    this.step.set(step);
    this.reached.update((set) => new Set<WizardStep>(set).add(step));
    this.focusAfterRender(focus);
  }

  private validateStep(step: FormStep): FieldErrors {
    const errors: FieldErrors = {};
    const values = this.form.getRawValue();

    for (const field of SIMPLE_FIELD_KEYS) {
      if (stepOfField(field) !== step) {
        continue;
      }
      const message = validateField(field, values);
      if (message !== null) {
        errors[field] = message;
      }
    }

    if (step === 2) {
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

  private focusAfterRender(target: FocusTarget): void {
    afterNextRender(
      () => {
        if (target === 'title') {
          this.hostElement.querySelector<HTMLElement>('#register-company-title')?.focus();
        } else if (target === 'invalid') {
          this.focusFirstInvalidNow();
        } else {
          this.focusSummaryNow();
        }
      },
      { injector: this.injector },
    );
  }

  private focusFirstInvalidNow(): void {
    const firstInvalid = this.hostElement.querySelector<HTMLElement>('[aria-invalid="true"]');
    if (firstInvalid === null) {
      this.focusSummaryNow();
      return;
    }
    // PrimeNG hosts carry the attribute; the focusable control is inside them.
    const focusable = firstInvalid.matches('input, button, select, textarea')
      ? firstInvalid
      : (firstInvalid.querySelector<HTMLElement>('input, [role="combobox"]') ?? firstInvalid);
    focusable.focus();
  }

  private focusSummaryNow(): void {
    this.hostElement.querySelector<HTMLElement>('#register-company-error-summary')?.focus();
  }
}

function withoutStep(errors: FieldErrors, step: FormStep): FieldErrors {
  const remaining: FieldErrors = {};
  for (const [field, message] of Object.entries(errors) as [FieldKey, string][]) {
    if (stepOfField(field) !== step) {
      remaining[field] = message;
    }
  }
  return remaining;
}

function buildRegisterCompanyForm(): FormGroup<RegisterCompanyFormControls> {
  const companyTimezone = browserTimezone();

  const form = new FormGroup({
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

  // FR-05: a closed day's dropdowns are disabled. Raw values still feed validation and the body.
  for (const day of WEEKDAYS) {
    const group = form.controls.branch.controls.businessHours.controls[day];
    const sync = (open: boolean): void => {
      if (open) {
        group.controls.start.enable({ emitEvent: false });
        group.controls.end.enable({ emitEvent: false });
      } else {
        group.controls.start.disable({ emitEvent: false });
        group.controls.end.disable({ emitEvent: false });
      }
    };
    sync(group.controls.open.value);
    group.controls.open.valueChanges.subscribe(sync);
  }

  return form;
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
