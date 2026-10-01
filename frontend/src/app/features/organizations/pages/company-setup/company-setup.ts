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
import { FormControl, FormGroup } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { ConfirmationService, MessageService } from 'primeng/api';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { Observable, map } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { AdministrationNav } from '../../components/administration-nav/administration-nav';
import { BranchDrawer } from '../../components/branch-drawer/branch-drawer';
import { BranchList } from '../../components/branch-list/branch-list';
import { CompanyProfileCard } from '../../components/company-profile-card/company-profile-card';
import { DocumentNumberingCard } from '../../components/document-numbering-card/document-numbering-card';
import { ErrorSummary, FieldErrorLink } from '../../components/error-summary/error-summary';
import {
  SequenceValues,
  SequencesDialog,
} from '../../components/sequences-dialog/sequences-dialog';
import { TaxesCurrencyCard } from '../../components/taxes-currency-card/taxes-currency-card';
import { countryOptions, currencyOptions, timezoneOptions } from '../../data/display-names';
import { SUPPORTED_COUNTRY_CODES } from '../../data/supported-countries';
import { SUPPORTED_CURRENCY_CODES } from '../../data/supported-currencies';
import {
  BranchListItem,
  CompanySetupFormControls,
  OrganizationFieldErrors,
  OrganizationFieldKey,
  OrganizationLogoMetadata,
  OrganizationSettingsResponse,
} from '../../models/company-settings.model';
import { BranchesService } from '../../services/branches.service';
import { OrganizationSettingsService } from '../../services/organization-settings.service';
import { handleUnauthorized } from '../../utils/handle-unauthorized';
import { fieldControlId } from '../register-company/register-company.validators';
import {
  ORGANIZATION_FIELD_KEYS,
  ORGANIZATION_FIELD_LABELS,
  OrganizationFormValue,
  buildOrganizationSettingsRequest,
  mapOrganizationServerFieldErrors,
  validateOrganizationField,
} from './company-setup.validators';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';

const ERROR_SUMMARY_ID = 'company-setup-error-summary';
/** Server errors for these keys live in the sequences dialog, not on a card field. */
const DIALOG_ONLY_KEYS: readonly OrganizationFieldKey[] = [
  'organization.nextQuoteNumber',
  'organization.nextWorkOrderNumber',
];

export const LOAD_ERROR_MESSAGE = "We couldn't load company settings.";
export const BRANCH_LOAD_ERROR_MESSAGE =
  "We couldn't load branches. Check your connection and try again.";
export const FORBIDDEN_MESSAGE = "You don't have access to company settings.";
export const READ_ONLY_MESSAGE =
  'You have view-only access to company settings. Contact an Owner to request changes.';
export const DESCRIPTION_MESSAGE =
  'Manage your organization details, branches, billing defaults, and document numbering.';
export const STALE_SETTINGS_MESSAGE =
  'This record was changed by someone else. Reload to see the latest version.';
export const SAVED_MESSAGE = 'Company settings saved';
/** Key of the currency confirmation, distinct from the un-keyed discard dialog. */
export const CURRENCY_DIALOG_KEY = 'currency';

/**
 * Company setup page (`/admin/company`): Administration column, profile (logo, address),
 * branches, taxes/currency and document numbering, sequences dialog, currency confirmation
 * and the branch drawer. Toast/confirm providers are page-level so children inherit them.
 */
@Component({
  selector: 'app-company-setup',
  imports: [
    ConfirmDialog,
    DiscardChangesDialog,
    RouterLink,
    ButtonDirective,
    SpinnerIcon,
    Skeleton,
    Toast,
    AdministrationNav,
    CompanyProfileCard,
    TaxesCurrencyCard,
    DocumentNumberingCard,
    ErrorSummary,
    BranchList,
    BranchDrawer,
    SequencesDialog,
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
  private readonly numbering = viewChild(DocumentNumberingCard);

  private submitAttempted = false;

  readonly errorSummaryId = ERROR_SUMMARY_ID;
  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly readOnlyMessage = READ_ONLY_MESSAGE;
  readonly descriptionMessage = DESCRIPTION_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly currencyDialogKey = CURRENCY_DIALOG_KEY;
  readonly timezoneOptionsList = timezoneOptions();
  readonly countryOptionsList = countryOptions(SUPPORTED_COUNTRY_CODES);
  readonly currencyOptionsList = currencyOptions(SUPPORTED_CURRENCY_CODES);

  readonly form: FormGroup<CompanySetupFormControls> = buildOrganizationForm();
  readonly formDirty = toSignal(this.form.valueChanges.pipe(map(() => this.form.dirty)), {
    initialValue: false,
  });

  readonly settingsLoading = signal(true);
  readonly settingsError = signal<ApiError | null>(null);
  readonly canManage = signal(false);
  readonly hasInvoices = signal(false);
  readonly logo = signal<OrganizationLogoMetadata | null>(null);
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

  readonly sequencesOpen = signal(false);
  readonly sequenceValues = signal<SequenceValues>({
    nextQuoteNumber: 1,
    nextWorkOrderNumber: 1,
    nextInvoiceNumber: 1,
  });

  readonly forbidden = computed(() => this.settingsError()?.kind === 'forbidden');
  readonly showForm = computed(
    () => !this.settingsLoading() && !this.forbidden() && this.settingsError() === null,
  );
  readonly canSave = computed(() => this.formDirty() && !this.submitting());

  readonly sequenceServerErrors = computed(() => {
    const errors = this.fieldErrors();
    return {
      nextQuoteNumber: errors['organization.nextQuoteNumber'],
      nextWorkOrderNumber: errors['organization.nextWorkOrderNumber'],
      nextInvoiceNumber: errors['organization.nextInvoiceNumber'],
    };
  });

  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    (Object.entries(this.fieldErrors()) as [OrganizationFieldKey, string][]).map(
      ([field, message]) => ({
        fieldId: fieldControlId(field),
        label: ORGANIZATION_FIELD_LABELS[field],
        message,
      }),
    ),
  );

  /** The values last confirmed by the server (initial load or a successful save); Discard target. */
  private readonly loadedFormValue = signal<OrganizationFormValue | null>(null);

  constructor() {
    this.loadSettings();
    this.loadBranches();
  }

  /** Consulted by `companySettingsUnsavedChangesGuard` on route leave. */
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
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this company profile',
          accept: () => {
            subscriber.next(true);
            subscriber.complete();
          },
          reject: () => {
            subscriber.next(false);
            subscriber.complete();
          },
        }),
      );
    });
  }

  discard(): void {
    if (!this.form.dirty) {
      return;
    }
    this.confirmationService.confirm(
      discardChangesConfirmation({
        subject: 'this company profile',
        accept: () => this.resetFormToLoaded(),
      }),
    );
  }

  onFieldBlur(field: OrganizationFieldKey): void {
    if (!this.submitAttempted) {
      return;
    }
    this.setFieldError(field, validateOrganizationField(field, this.form.getRawValue()));
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

    if (this.currencyChanged() && this.hasInvoices()) {
      this.confirmCurrencyChange();
      return;
    }
    this.send(false);
  }

  private currencyChanged(): boolean {
    const loaded = this.loadedFormValue()?.currency.trim().toUpperCase();
    return this.form.getRawValue().currency.trim().toUpperCase() !== loaded;
  }

  /** BR-06: the confirmation is a keyed dialog, distinct from the discard prompt. Cancel sends nothing. */
  private confirmCurrencyChange(): void {
    const code = this.form.getRawValue().currency.trim().toUpperCase();
    this.confirmationService.confirm({
      key: CURRENCY_DIALOG_KEY,
      header: 'Change currency',
      message: `Change currency to ${code}? Existing invoices keep their original currency.`,
      acceptButtonProps: { label: 'Change currency' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.send(true),
    });
  }

  private send(confirmed: boolean): void {
    this.submitting.set(true);
    const request = buildOrganizationSettingsRequest(
      this.form.getRawValue(),
      this.loadedUpdatedAt() ?? '',
      confirmed,
    );

    this.settingsService
      .update(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.submitting.set(false);
          this.loadedUpdatedAt.set(response.updatedAt);
          this.hasInvoices.set(response.hasInvoices);
          const value = toFormValue(response);
          this.loadedFormValue.set(value);
          this.form.reset(value);
          this.messageService.add({ severity: 'success', summary: SAVED_MESSAGE });
          this.sessionService.loadCurrent().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
        },
        error: (error: unknown) => this.handleSaveFailed(error, confirmed),
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

  openSequences(): void {
    const value = this.form.getRawValue();
    this.sequenceValues.set({
      nextQuoteNumber: value.nextQuoteNumber,
      nextWorkOrderNumber: value.nextWorkOrderNumber,
      nextInvoiceNumber: value.nextInvoiceNumber,
    });
    this.sequencesOpen.set(true);
  }

  /** Apply: the valid values go into the page form (dirty); they persist with Save changes. */
  onSequencesApplied(values: SequenceValues): void {
    this.form.markAsDirty();
    this.form.patchValue(values);
    this.fieldErrors.update((errors) => {
      const next = { ...errors };
      delete next['organization.nextQuoteNumber'];
      delete next['organization.nextWorkOrderNumber'];
      delete next['organization.nextInvoiceNumber'];
      return next;
    });
  }

  onSequencesClosed(): void {
    afterNextRender(() => this.numbering()?.focusSequencesLink(), { injector: this.injector });
  }

  openAddBranch(): void {
    this.switchBranch(null);
  }

  openBranch(id: string): void {
    this.switchBranch(id);
  }

  /** Selecting another branch while the drawer has unsaved edits asks first (dirty guard). */
  private switchBranch(id: string | null): void {
    if (this.drawerOpen() && (this.drawer()?.isDirty() ?? false)) {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this branch',
          accept: () => this.showBranch(id),
        }),
      );
      return;
    }
    this.showBranch(id);
  }

  private showBranch(id: string | null): void {
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
          this.hasInvoices.set(response.hasInvoices);
          this.logo.set(response.logo);
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

  private handleSaveFailed(error: unknown, confirmed: boolean): void {
    this.submitting.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;

    if (apiError?.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    if (apiError?.kind === 'conflict') {
      // BR-06: the server requires confirmation; ask once, then resend with the flag.
      if (apiError.fieldErrors['currency'] !== undefined && !confirmed) {
        this.confirmCurrencyChange();
        return;
      }
      this.staleConflict.set(true);
      this.pageMessage.set(STALE_SETTINGS_MESSAGE);
      this.focusSummary();
      return;
    }
    if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const mapped = mapOrganizationServerFieldErrors(apiError.fieldErrors);
      this.fieldErrors.set(mapped);
      this.pageMessage.set(apiError.message);
      const first = Object.keys(mapped)[0] as OrganizationFieldKey | undefined;
      if (first === undefined) {
        this.focusSummary();
      } else if (DIALOG_ONLY_KEYS.includes(first)) {
        afterNextRender(() => this.numbering()?.focusSequencesLink(), { injector: this.injector });
      } else {
        this.focusFirstInvalid();
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
        } else if (
          DIALOG_ONLY_KEYS.some((key) => this.fieldErrors()[key] !== undefined) &&
          this.numbering() !== undefined
        ) {
          this.numbering()?.focusSequencesLink();
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

function buildOrganizationForm(): FormGroup<CompanySetupFormControls> {
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
    website: new FormControl('', { nonNullable: true }),
    addressLine1: new FormControl('', { nonNullable: true }),
    city: new FormControl('', { nonNullable: true }),
    stateRegion: new FormControl('', { nonNullable: true }),
    postalCode: new FormControl('', { nonNullable: true }),
    countryCode: new FormControl('', { nonNullable: true }),
    pricesIncludeTax: new FormControl(false, { nonNullable: true }),
    nextQuoteNumber: new FormControl<number | null>(1),
    nextWorkOrderNumber: new FormControl<number | null>(1),
  });
}

function toFormValue(response: OrganizationSettingsResponse): OrganizationFormValue {
  return {
    name: response.name,
    legalName: response.legalName ?? '',
    taxId: response.taxId ?? '',
    email: response.email ?? '',
    phone: response.phone ?? '',
    timezone: response.timezone,
    currency: response.currency,
    defaultTaxRate: response.defaultTaxRate,
    quotePrefix: response.quotePrefix,
    workOrderPrefix: response.workOrderPrefix,
    invoicePrefix: response.invoicePrefix,
    nextInvoiceNumber: response.nextInvoiceNumber,
    website: response.website ?? '',
    addressLine1: response.addressLine1 ?? '',
    city: response.city ?? '',
    stateRegion: response.stateRegion ?? '',
    postalCode: response.postalCode ?? '',
    countryCode: response.countryCode ?? '',
    pricesIncludeTax: response.pricesIncludeTax,
    nextQuoteNumber: response.nextQuoteNumber,
    nextWorkOrderNumber: response.nextWorkOrderNumber,
  };
}
