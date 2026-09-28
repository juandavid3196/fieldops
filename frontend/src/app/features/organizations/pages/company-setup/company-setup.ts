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
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { Observable, map } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { BranchDrawer } from '../../components/branch-drawer/branch-drawer';
import { BranchList } from '../../components/branch-list/branch-list';
import { CompanyProfileSection } from '../../components/company-profile-section/company-profile-section';
import { DocumentNumberingSection } from '../../components/document-numbering-section/document-numbering-section';
import { ErrorSummary, FieldErrorLink } from '../../components/error-summary/error-summary';
import { TaxesCurrencySection } from '../../components/taxes-currency-section/taxes-currency-section';
import { SUPPORTED_COUNTRY_CODES } from '../../data/supported-countries';
import { SUPPORTED_CURRENCY_CODES } from '../../data/supported-currencies';
import { countryOptions, currencyOptions, timezoneOptions } from '../../data/display-names';
import {
  BranchListItem,
  OrganizationFieldErrors,
  OrganizationFieldKey,
} from '../../models/company-settings.model';
import { OrganizationFormControls } from '../../models/organization-registration.model';
import { FIELD_LABELS, fieldControlId } from '../register-company/register-company.validators';
import { OrganizationSettingsService } from '../../services/organization-settings.service';
import { BranchesService } from '../../services/branches.service';
import { SessionService } from '../../../../core/services/session.service';
import { handleUnauthorized } from '../../utils/handle-unauthorized';
import {
  ORGANIZATION_FIELD_KEYS,
  OrganizationFormValue,
  buildOrganizationSettingsRequest,
  mapOrganizationServerFieldErrors,
  validateOrganizationField,
} from './company-setup.validators';

import type { SimpleFieldKey as RegistrationFieldKey } from '../../models/organization-registration.model';

const ERROR_SUMMARY_ID = 'company-setup-error-summary';

export const LOAD_ERROR_MESSAGE = "We couldn't load company settings.";
export const BRANCH_LOAD_ERROR_MESSAGE = "We couldn't load branches.";
export const FORBIDDEN_MESSAGE = "You don't have access to company settings.";
export const READ_ONLY_MESSAGE = 'You have view-only access to company settings.';
export const STALE_SETTINGS_MESSAGE =
  'This record was changed by someone else. Reload to see the latest version.';
export const SAVED_MESSAGE = 'Company settings saved';

/**
 * Company setup page (`/admin/company`, FR-01, FR-12 to FR-19): organization
 * settings plus the branch list and drawer. Toast/confirm providers are
 * page-level (OD-25) so `BranchList`/`BranchDrawer` inherit them via DI.
 */
@Component({
  selector: 'app-company-setup',
  imports: [
    ReactiveFormsModule,
    ButtonDirective,
    ConfirmDialog,
    SpinnerIcon,
    Skeleton,
    Toast,
    CompanyProfileSection,
    TaxesCurrencySection,
    DocumentNumberingSection,
    ErrorSummary,
    BranchList,
    BranchDrawer,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './company-setup.html',
  styleUrl: './company-setup.scss',
})
export class CompanySetup {
  private readonly settingsService = inject(OrganizationSettingsService);
  private readonly branchesService = inject(BranchesService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  private readonly drawer = viewChild(BranchDrawer);

  private submitAttempted = false;

  readonly errorSummaryId = ERROR_SUMMARY_ID;
  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly readOnlyMessage = READ_ONLY_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly timezoneOptionsList = timezoneOptions();
  readonly countryOptionsList = countryOptions(SUPPORTED_COUNTRY_CODES);
  readonly currencyOptionsList = currencyOptions(SUPPORTED_CURRENCY_CODES);

  readonly form: FormGroup<OrganizationFormControls> = buildOrganizationForm();
  readonly formDirty = toSignal(this.form.valueChanges.pipe(map(() => this.form.dirty)), {
    initialValue: false,
  });

  readonly settingsLoading = signal(true);
  readonly settingsError = signal<ApiError | null>(null);
  readonly canManage = signal(false);
  readonly loadedUpdatedAt = signal<string | null>(null);
  readonly fieldErrors = signal<OrganizationFieldErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly staleConflict = signal(false);
  readonly sessionExpired = signal(false);

  readonly branchesLoading = signal(true);
  readonly branchesError = signal<ApiError | null>(null);
  readonly branches = signal<readonly BranchListItem[]>([]);

  readonly drawerOpen = signal(false);
  readonly selectedBranchId = signal<string | null>(null);

  readonly forbidden = computed(() => this.settingsError()?.kind === 'forbidden');
  readonly showForm = computed(
    () => !this.settingsLoading() && !this.forbidden() && this.settingsError() === null,
  );
  readonly canSave = computed(() => this.formDirty() && !this.submitting());

  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    (Object.entries(this.fieldErrors()) as [OrganizationFieldKey, string][]).map(
      ([field, message]) => ({
        fieldId: fieldControlId(field),
        label: FIELD_LABELS[field],
        message,
      }),
    ),
  );

  /** The values last confirmed by the server (initial load or a successful save); FR-13 Discard target. */
  private readonly loadedFormValue = signal<OrganizationFormValue | null>(null);

  constructor() {
    this.loadSettings();
    this.loadBranches();
  }

  /** FR-17: consulted by `companySettingsUnsavedChangesGuard` on route leave. */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired()) {
      return true;
    }
    const dirty = this.formDirty() || (this.drawerOpen() && (this.drawer()?.isDirty() ?? false));
    if (!dirty) {
      return true;
    }
    return this.confirmDiscard();
  }

  private confirmDiscard(): Observable<boolean> {
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm({
        header: 'Discard unsaved changes?',
        message: 'You have unsaved changes. Do you want to discard them?',
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

  discard(): void {
    if (!this.form.dirty) {
      return;
    }
    this.confirmationService.confirm({
      header: 'Discard unsaved changes?',
      message: 'You have unsaved changes. Do you want to discard them?',
      acceptButtonProps: { label: 'Discard', severity: 'danger' },
      rejectButtonProps: { label: 'Keep editing', severity: 'secondary', outlined: true },
      accept: () => this.resetFormToLoaded(),
    });
  }

  onFieldBlur(field: RegistrationFieldKey): void {
    if (!this.submitAttempted) {
      return;
    }
    const key = field as OrganizationFieldKey;
    const message = validateOrganizationField(key, this.form.getRawValue());
    this.setFieldError(key, message);
  }

  save(): void {
    if (this.submitting()) {
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
    const request = buildOrganizationSettingsRequest(
      this.form.getRawValue(),
      this.loadedUpdatedAt() ?? '',
    );

    this.settingsService
      .update(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.submitting.set(false);
          this.loadedUpdatedAt.set(response.updatedAt);
          const value = toFormValue(response);
          this.loadedFormValue.set(value);
          this.form.reset(value);
          this.messageService.add({ severity: 'success', summary: SAVED_MESSAGE });
          this.sessionService.loadCurrent().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
        },
        error: (error: unknown) => this.handleSaveFailed(error),
      });
  }

  reloadStale(): void {
    this.staleConflict.set(false);
    this.pageMessage.set(null);
    this.loadSettings();
  }

  retrySettings(): void {
    this.loadSettings();
  }

  retryBranches(): void {
    this.loadBranches();
  }

  openAddBranch(): void {
    this.selectedBranchId.set(null);
    this.drawerOpen.set(true);
  }

  openBranch(id: string): void {
    this.selectedBranchId.set(id);
    this.drawerOpen.set(true);
  }

  onDrawerClosed(): void {
    this.drawerOpen.set(false);
  }

  onBranchesChanged(): void {
    this.loadBranches();
  }

  onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  private loadSettings(): void {
    this.settingsLoading.set(true);
    this.settingsError.set(null);
    this.settingsService
      .get()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.settingsLoading.set(false);
          this.canManage.set(response.canManage);
          this.loadedUpdatedAt.set(response.updatedAt);
          this.fieldErrors.set({});
          this.pageMessage.set(null);
          this.submitAttempted = false;
          const value = toFormValue(response);
          this.loadedFormValue.set(value);
          this.form.reset(value);
          if (!response.canManage) {
            this.form.disable({ emitEvent: false });
          } else {
            this.form.enable({ emitEvent: false });
          }
        },
        error: (error: unknown) => this.handleSettingsLoadFailed(error),
      });
  }

  private loadBranches(): void {
    this.branchesLoading.set(true);
    this.branchesError.set(null);
    this.branchesService
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.branchesLoading.set(false);
          this.branches.set(response.items);
        },
        error: (error: unknown) => this.handleBranchesLoadFailed(error),
      });
  }

  private handleSettingsLoadFailed(error: unknown): void {
    this.settingsLoading.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    this.settingsError.set(
      apiError ?? { kind: 'unknown', status: 0, message: LOAD_ERROR_MESSAGE, fieldErrors: {} },
    );
  }

  private handleBranchesLoadFailed(error: unknown): void {
    this.branchesLoading.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    this.branchesError.set(
      apiError ?? {
        kind: 'unknown',
        status: 0,
        message: BRANCH_LOAD_ERROR_MESSAGE,
        fieldErrors: {},
      },
    );
  }

  private handleSaveFailed(error: unknown): void {
    this.submitting.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;

    if (apiError?.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    if (apiError?.kind === 'conflict') {
      this.staleConflict.set(true);
      this.pageMessage.set(STALE_SETTINGS_MESSAGE);
      this.focusSummary();
      return;
    }
    if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const mapped = mapOrganizationServerFieldErrors(apiError.fieldErrors);
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

  private resetFormToLoaded(): void {
    const value = this.loadedFormValue();
    if (value === null) {
      return;
    }
    this.fieldErrors.set({});
    this.pageMessage.set(null);
    this.submitAttempted = false;
    this.form.reset(value);
  }

  private validateForm(): OrganizationFieldErrors {
    const errors: OrganizationFieldErrors = {};
    const value = this.form.getRawValue();
    for (const field of ORGANIZATION_FIELD_KEYS) {
      const message = validateOrganizationField(field, value);
      if (message !== null) {
        errors[field] = message;
      }
    }
    return errors;
  }

  private setFieldError(field: OrganizationFieldKey, message: string | null): void {
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
    this.hostElement.querySelector<HTMLElement>(`#${ERROR_SUMMARY_ID}`)?.focus();
  }
}

function buildOrganizationForm(): FormGroup<OrganizationFormControls> {
  return new FormGroup({
    name: new FormControl('', { nonNullable: true }),
    legalName: new FormControl('', { nonNullable: true }),
    taxId: new FormControl('', { nonNullable: true }),
    email: new FormControl('', { nonNullable: true }),
    phone: new FormControl('', { nonNullable: true }),
    timezone: new FormControl('', { nonNullable: true }),
    currency: new FormControl('', { nonNullable: true }),
    defaultTaxRate: new FormControl<number | null>(0),
    quotePrefix: new FormControl('', { nonNullable: true }),
    workOrderPrefix: new FormControl('', { nonNullable: true }),
    invoicePrefix: new FormControl('', { nonNullable: true }),
    nextInvoiceNumber: new FormControl<number | null>(1),
  });
}

function toFormValue(response: {
  name: string;
  legalName: string;
  taxId: string;
  email: string;
  phone: string;
  timezone: string;
  currency: string;
  defaultTaxRate: number;
  quotePrefix: string;
  workOrderPrefix: string;
  invoicePrefix: string;
  nextInvoiceNumber: number;
}): OrganizationFormValue {
  return {
    name: response.name,
    legalName: response.legalName,
    taxId: response.taxId,
    email: response.email,
    phone: response.phone,
    timezone: response.timezone,
    currency: response.currency,
    defaultTaxRate: response.defaultTaxRate,
    quotePrefix: response.quotePrefix,
    workOrderPrefix: response.workOrderPrefix,
    invoicePrefix: response.invoicePrefix,
    nextInvoiceNumber: response.nextInvoiceNumber,
  };
}
