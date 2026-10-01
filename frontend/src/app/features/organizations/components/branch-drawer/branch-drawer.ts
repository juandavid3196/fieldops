import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { ConfirmationService, MessageService } from 'primeng/api';
import { InputText } from 'primeng/inputtext';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { TooltipModule } from 'primeng/tooltip';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { BusinessHoursEditor } from '../business-hours-editor/business-hours-editor';
import { ErrorSummary, FieldErrorLink } from '../error-summary/error-summary';
import { FormField } from '../form-field/form-field';
import { SelectOption } from '../../data/display-names';
import {
  BranchDetail,
  BranchDrawerFormControls,
  BranchDrawerFormValue,
  BranchFieldErrors,
  BranchFieldKey,
  BranchListItem,
  BranchSimpleFieldKey,
  buildBranchRequest,
  formatServicePostalCodes,
} from '../../models/company-settings.model';
import {
  BusinessHoursControls,
  BusinessHoursPayload,
  WEEKDAYS,
} from '../../models/organization-registration.model';
import {
  businessHoursEndKey,
  businessHoursStartKey,
  fieldControlId,
  validateBusinessHoursEnd,
  validateBusinessHoursStart,
} from '../../pages/register-company/register-company.validators';
import { BranchesService } from '../../services/branches.service';
import { MAIN_BRANCH_TOOLTIP } from '../branch-list/branch-list';
import {
  BRANCH_FIELD_LABELS,
  BRANCH_SIMPLE_FIELD_KEYS,
  mapBranchServerFieldErrors,
  validateBranchField,
} from './branch-drawer.validators';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';

const BUSINESS_HOURS_FIELD_PATTERN = /^branch\.businessHours\.(\w+)\.(start|end)$/;
const DRAWER_ERROR_SUMMARY_ID = 'branch-drawer-error-summary';
const DRAWER_TITLE_ID = 'branch-drawer-title';

/** BR-15 copy shown for a branch load error (other than 401/404, handled separately). */
export const BRANCH_LOAD_ERROR_MESSAGE = "We couldn't load this branch.";
export const BRANCH_NOT_FOUND_MESSAGE = 'This branch is no longer available.';
export const STALE_BRANCH_MESSAGE =
  'This record was changed by someone else. Reload to see the latest version.';
export const BRANCH_LIMIT_MESSAGE = 'Your organization has reached the limit of 100 branches.';
export const ONLY_ACTIVE_BRANCH_TOOLTIP = 'At least one branch must stay active.';

/**
 * Create/edit/view branch drawer (FR-15, FR-16): a self-contained scrollable
 * form plus the deactivate/reactivate footer action (D7: pre-disabled with
 * the BR-06 tooltip whenever the open branch is the organization's only
 * active one, from the same loaded `branches` list the row menu reads).
 */
@Component({
  selector: 'app-branch-drawer',
  imports: [
    ReactiveFormsModule,
    ButtonDirective,
    InputText,
    Select,
    SpinnerIcon,
    Skeleton,
    Tag,
    ToggleSwitch,
    TooltipModule,
    DrawerShell,
    BusinessHoursEditor,
    ErrorSummary,
    FormField,
  ],
  templateUrl: './branch-drawer.html',
  styleUrl: './branch-drawer.scss',
})
export class BranchDrawer {
  private readonly branchesService = inject(BranchesService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly open = input.required<boolean>();
  readonly branchId = input<string | null>(null);
  readonly canManage = input.required<boolean>();
  readonly organizationTimezone = input.required<string>();
  readonly timezoneOptions = input.required<SelectOption[]>();
  readonly countryOptions = input.required<SelectOption[]>();
  readonly branches = input.required<readonly BranchListItem[]>();

  readonly saved = output<void>();
  readonly closed = output<void>();
  /** FR-19: any request the drawer makes returned `401`; the page owns the redirect. */
  readonly unauthorized = output<void>();

  readonly errorSummaryId = DRAWER_ERROR_SUMMARY_ID;
  readonly titleId = DRAWER_TITLE_ID;
  readonly onlyActiveTooltip = ONLY_ACTIVE_BRANCH_TOOLTIP;
  readonly loadErrorMessage = BRANCH_LOAD_ERROR_MESSAGE;
  readonly form: FormGroup<BranchDrawerFormControls> = buildBranchDrawerForm('');

  private readonly shell = viewChild(DrawerShell);
  /** FR-16: docked from 1440px, an overlay below the top bar from 768px, full screen below. */
  readonly mode = computed(() => this.shell()?.mode() ?? 'overlay');

  readonly detail = signal<BranchDetail | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal<ApiError | null>(null);
  readonly fieldErrors = signal<BranchFieldErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly deactivating = signal(false);
  readonly staleConflict = signal(false);

  private submitAttempted = false;
  private lastLoadedUpdatedAt: string | null = null;

  readonly isCreate = computed(() => this.branchId() === null);
  readonly title = computed(() => (this.isCreate() ? 'New branch' : (this.detail()?.name ?? '')));
  readonly readOnly = computed(() => !this.canManage());
  readonly isMain = computed(() => this.detail()?.isMain ?? false);
  readonly mainTooltip = MAIN_BRANCH_TOOLTIP;

  readonly onlyActiveBranch = computed(() => {
    const id = this.branchId();
    if (id === null) {
      return false;
    }
    const active = this.branches().filter((branch) => branch.isActive);
    return active.length === 1 && active[0].id === id;
  });

  /** Normalizes `undefined` (no error) to `null` so template checks never see `undefined !== null`. */
  error(field: BranchFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    (Object.entries(this.fieldErrors()) as [BranchFieldKey, string][])
      .filter(([field]) => field !== 'branch.businessHours')
      .map(([field, message]) => ({
        fieldId: fieldControlId(field),
        label: BRANCH_FIELD_LABELS[field],
        message,
      })),
  );

  constructor() {
    effect(() => {
      if (this.open()) {
        this.initialize(this.branchId());
      }
    });
  }

  private initialize(branchId: string | null): void {
    this.submitAttempted = false;
    this.fieldErrors.set({});
    this.pageMessage.set(null);
    this.staleConflict.set(false);
    this.loadError.set(null);
    this.detail.set(null);

    if (branchId === null) {
      this.resetForm(createFormValue(this.organizationTimezone()));
      this.lastLoadedUpdatedAt = null;
      this.applyReadOnly();
      return;
    }

    this.loading.set(true);
    this.branchesService
      .get(branchId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (branch) => this.handleLoaded(branch),
        error: (error: unknown) => this.handleLoadFailed(error),
      });
  }

  private handleLoaded(branch: BranchDetail): void {
    this.loading.set(false);
    this.detail.set(branch);
    this.lastLoadedUpdatedAt = branch.updatedAt;
    this.resetForm(editFormValue(branch));
    this.applyReadOnly();
  }

  private applyReadOnly(): void {
    if (this.readOnly()) {
      this.form.disable({ emitEvent: false });
    } else {
      this.form.enable({ emitEvent: false });
    }
  }

  private handleLoadFailed(error: unknown): void {
    this.loading.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;

    if (apiError?.kind === 'unauthorized') {
      this.signalUnauthorized();
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.closed.emit();
      this.messageService.add({ severity: 'error', summary: BRANCH_NOT_FOUND_MESSAGE });
      this.saved.emit();
      return;
    }
    this.loadError.set(
      apiError ?? {
        kind: 'unknown',
        status: 0,
        message: BRANCH_LOAD_ERROR_MESSAGE,
        fieldErrors: {},
      },
    );
  }

  retryLoad(): void {
    const id = this.branchId();
    if (id !== null) {
      this.initialize(id);
    }
  }

  /**
   * `string`, not `BranchFieldKey`: the reused `business-hours-editor` emits
   * its own (register-company) `FieldKey` type, which overlaps with but isn't
   * identical to this drawer's key namespace (it lacks `branch.addressLine2`).
   * Every runtime value is still one of this drawer's known keys.
   */
  onFieldBlur(field: string): void {
    if (!this.submitAttempted || this.readOnly()) {
      return;
    }
    const key = field as BranchFieldKey;
    this.setFieldError(key, this.computeFieldError(key));
  }

  submit(): void {
    if (this.submitting() || this.readOnly()) {
      return;
    }

    this.submitAttempted = true;
    this.pageMessage.set(null);
    this.staleConflict.set(false);

    const errors = this.validateForm();
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusFirstInvalid();
      return;
    }

    this.submitting.set(true);
    const request = buildBranchRequest(this.form.getRawValue() as BranchDrawerFormValue);
    const id = this.branchId();

    const request$ =
      id === null
        ? this.branchesService.create(request)
        : this.branchesService.update(id, {
            ...request,
            updatedAt: this.lastLoadedUpdatedAt ?? '',
          });

    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (branch) => this.handleSaved(branch),
      error: (error: unknown) => this.handleSaveFailed(error),
    });
  }

  private handleSaved(branch: BranchDetail): void {
    this.submitting.set(false);
    const verb = this.isCreate() ? 'created' : 'updated';
    this.messageService.add({ severity: 'success', summary: `${branch.name} ${verb}` });
    this.closed.emit();
    this.saved.emit();
  }

  private handleSaveFailed(error: unknown): void {
    this.submitting.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;

    if (apiError?.kind === 'unauthorized') {
      this.signalUnauthorized();
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.closed.emit();
      this.messageService.add({ severity: 'error', summary: BRANCH_NOT_FOUND_MESSAGE });
      this.saved.emit();
      return;
    }
    if (apiError?.kind === 'conflict') {
      const hasFieldErrors = Object.keys(apiError.fieldErrors).length > 0;
      if (hasFieldErrors) {
        this.fieldErrors.set(mapBranchServerFieldErrors(apiError.fieldErrors));
        this.focusFirstInvalid();
        return;
      }
      if (this.isCreate()) {
        this.pageMessage.set(BRANCH_LIMIT_MESSAGE);
      } else {
        this.staleConflict.set(true);
        this.pageMessage.set(STALE_BRANCH_MESSAGE);
      }
      this.focusSummary();
      return;
    }
    if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const mapped = mapBranchServerFieldErrors(apiError.fieldErrors);
      this.fieldErrors.set(mapped);
      this.pageMessage.set(apiError.message);
      if (Object.keys(mapped).length > 0) {
        this.focusFirstInvalid();
      } else {
        this.focusSummary();
      }
      return;
    }

    this.pageMessage.set(apiError?.message ?? 'An unexpected error occurred. Please try again.');
    this.focusSummary();
  }

  reload(): void {
    const id = this.branchId();
    if (id !== null) {
      this.initialize(id);
    }
  }

  deactivateOrReactivate(): void {
    const branch = this.detail();
    const id = this.branchId();
    if (branch === null || id === null || this.deactivating() || this.readOnly()) {
      return;
    }

    if (!branch.isActive) {
      this.performStateChange(id, 'reactivate', branch.name);
      return;
    }

    if (this.onlyActiveBranch() || branch.isMain) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Deactivate branch',
      message: `Deactivate ${branch.name}? No new requests, quotes or work orders can be created for this branch. Existing records stay available.`,
      acceptButtonProps: { label: 'Deactivate', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.performStateChange(id, 'deactivate', branch.name),
    });
  }

  private performStateChange(id: string, action: 'deactivate' | 'reactivate', name: string): void {
    this.deactivating.set(true);
    const request$ =
      action === 'deactivate'
        ? this.branchesService.deactivate(id)
        : this.branchesService.reactivate(id);

    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.deactivating.set(false);
        const verb = action === 'deactivate' ? 'deactivated' : 'reactivated';
        this.messageService.add({ severity: 'success', summary: `${name} ${verb}` });
        this.closed.emit();
        this.saved.emit();
      },
      error: (error: unknown) => this.handleStateChangeFailed(error),
    });
  }

  private handleStateChangeFailed(error: unknown): void {
    this.deactivating.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;

    if (apiError?.kind === 'unauthorized') {
      this.signalUnauthorized();
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.closed.emit();
      this.messageService.add({ severity: 'error', summary: BRANCH_NOT_FOUND_MESSAGE });
      this.saved.emit();
      return;
    }
    if (apiError?.kind === 'conflict') {
      this.messageService.add({
        severity: 'error',
        summary: this.isMain() ? MAIN_BRANCH_TOOLTIP : ONLY_ACTIVE_BRANCH_TOOLTIP,
      });
      return;
    }
    this.messageService.add({
      severity: 'error',
      summary: apiError?.message ?? 'An unexpected error occurred. Please try again.',
    });
  }

  /** Read by the page's route-leave guard (FR-17, AC-29); ignored while read-only. */
  isDirty(): boolean {
    return !this.readOnly() && this.form.dirty;
  }

  requestClose(): void {
    if (this.form.dirty && !this.readOnly()) {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this branch',
          accept: () => this.closed.emit(),
        }),
      );
      return;
    }
    this.closed.emit();
  }

  private signalUnauthorized(): void {
    this.unauthorized.emit();
  }

  private resetForm(value: BranchDrawerFormValue): void {
    this.form.reset(value);
  }

  private validateForm(): BranchFieldErrors {
    const errors: BranchFieldErrors = {};
    const value = this.form.getRawValue() as BranchDrawerFormValue;

    for (const field of BRANCH_SIMPLE_FIELD_KEYS) {
      const message = validateBranchField(field, value);
      if (message !== null) {
        errors[field] = message;
      }
    }

    for (const day of WEEKDAYS) {
      const dayValue = value.businessHours[day];
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

  private computeFieldError(field: BranchFieldKey): string | null {
    const match = BUSINESS_HOURS_FIELD_PATTERN.exec(field);
    if (match) {
      const day = match[1] as (typeof WEEKDAYS)[number];
      const part = match[2] as 'start' | 'end';
      const dayGroup = this.form.controls.businessHours.controls[day];
      if (!dayGroup.controls.open.value) {
        return null;
      }
      return part === 'start'
        ? validateBusinessHoursStart(dayGroup.controls.start.value)
        : validateBusinessHoursEnd(dayGroup.controls.start.value, dayGroup.controls.end.value);
    }
    if (field === 'branch.businessHours') {
      return null;
    }
    return validateBranchField(
      field as BranchSimpleFieldKey,
      this.form.getRawValue() as BranchDrawerFormValue,
    );
  }

  private setFieldError(field: BranchFieldKey, message: string | null): void {
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
    this.hostElement.querySelector<HTMLElement>(`#${DRAWER_ERROR_SUMMARY_ID}`)?.focus();
  }
}

function buildBranchDrawerForm(timezone: string): FormGroup<BranchDrawerFormControls> {
  return new FormGroup({
    name: new FormControl('', { nonNullable: true }),
    code: new FormControl('', { nonNullable: true }),
    phone: new FormControl('', { nonNullable: true }),
    email: new FormControl('', { nonNullable: true }),
    timezone: new FormControl(timezone, { nonNullable: true }),
    addressLine1: new FormControl('', { nonNullable: true }),
    addressLine2: new FormControl('', { nonNullable: true }),
    city: new FormControl('', { nonNullable: true }),
    stateRegion: new FormControl('', { nonNullable: true }),
    postalCode: new FormControl('', { nonNullable: true }),
    countryCode: new FormControl('', { nonNullable: true }),
    servicePostalCodes: new FormControl('', { nonNullable: true }),
    usesCompanyBilling: new FormControl(true, { nonNullable: true }),
    businessHours: new FormGroup(
      Object.fromEntries(
        WEEKDAYS.map((day) => [day, businessHoursDayGroup(false, '09:00', '17:00')]),
      ) as BusinessHoursControls,
    ),
  });
}

function businessHoursDayGroup(open: boolean, start: string, end: string) {
  return new FormGroup({
    open: new FormControl(open, { nonNullable: true }),
    start: new FormControl(start, { nonNullable: true }),
    end: new FormControl(end, { nonNullable: true }),
  });
}

function createFormValue(timezone: string): BranchDrawerFormValue {
  return {
    name: '',
    code: '',
    phone: '',
    email: '',
    timezone,
    addressLine1: '',
    addressLine2: '',
    city: '',
    stateRegion: '',
    postalCode: '',
    countryCode: '',
    servicePostalCodes: '',
    usesCompanyBilling: true,
    businessHours: Object.fromEntries(
      WEEKDAYS.map((day) => [day, { open: false, start: '09:00', end: '17:00' }]),
    ) as BranchDrawerFormValue['businessHours'],
  };
}

function editFormValue(branch: BranchDetail): BranchDrawerFormValue {
  return {
    name: branch.name,
    code: branch.code,
    phone: branch.phone ?? '',
    email: branch.email ?? '',
    timezone: branch.timezone,
    addressLine1: branch.addressLine1,
    addressLine2: branch.addressLine2 ?? '',
    city: branch.city,
    stateRegion: branch.stateRegion ?? '',
    postalCode: branch.postalCode,
    countryCode: branch.countryCode,
    servicePostalCodes: formatServicePostalCodes(branch.servicePostalCodes),
    usesCompanyBilling: branch.usesCompanyBilling,
    businessHours: businessHoursFormValue(branch.businessHours),
  };
}

function businessHoursFormValue(
  payload: BusinessHoursPayload,
): BranchDrawerFormValue['businessHours'] {
  return Object.fromEntries(
    WEEKDAYS.map((day) => {
      const dayValue = payload[day];
      return [
        day,
        dayValue
          ? { open: true, start: dayValue.start, end: dayValue.end }
          : { open: false, start: '09:00', end: '17:00' },
      ];
    }),
  ) as BranchDrawerFormValue['businessHours'];
}
