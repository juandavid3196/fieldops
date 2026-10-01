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
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';
import { Observable, Subject, Subscription, catchError, map, of, switchMap, timer } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import {
  ErrorSummary,
  FieldErrorLink,
} from '../../../organizations/components/error-summary/error-summary';
import { FormField } from '../../../organizations/components/form-field/form-field';
import { US_STATE_OPTIONS } from '../../../organizations/data/us-states';
import {
  CUSTOMER_FIELD_KEYS,
  CustomerDetail,
  CustomerFieldErrors,
  CustomerFieldKey,
  CustomerRequest,
  CustomerTag,
  CustomerType,
  DuplicateCheckRequest,
  DuplicateMatch,
  MAX_TAGS,
} from '../../models/customer.model';
import { CustomersService } from '../../services/customers.service';
import {
  STATUS_LABELS,
  formatPhone,
  normalizeEmail,
  normalizePhone,
  propertyCountText,
} from '../../utils/customer-format';
import { TagSelector } from '../tag-selector/tag-selector';
import {
  CustomerFormValue,
  FIELD_LABELS,
  mapServerFieldErrors,
  validateCustomer,
  validateCustomerField,
} from './customer-drawer.validators';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';

export type CustomerDrawerMode = 'create' | 'edit' | 'view';

export interface BranchOption {
  readonly id: string;
  readonly name: string;
}

export const CUSTOMER_UNAVAILABLE_MESSAGE = 'This customer is no longer available.';
export const SAVE_FAILED_MESSAGE = "We couldn't save the customer. Try again.";
export const RESOLVE_MATCH_MESSAGE = 'Resolve the possible match to continue.';
const TAG_FAILED_MESSAGE = "We couldn't create the tag. Try again.";
const DUPLICATE_IDLE_MS = 500;
const TITLE_ID = 'customer-drawer-title';
const SUMMARY_ID = 'customer-drawer-error-summary';
const CONTROL_IDS: Readonly<Record<CustomerFieldKey, string>> = {
  type: 'customer-type-residential',
  companyName: 'customer-company-name',
  firstName: 'customer-first-name',
  lastName: 'customer-last-name',
  title: 'customer-title',
  email: 'customer-email',
  phone: 'customer-phone',
  preferences: 'customer-preferences',
  addressLine1: 'customer-address',
  city: 'customer-city',
  stateRegion: 'customer-state',
  postalCode: 'customer-zip',
  serviceInstructions: 'customer-instructions',
  branchId: 'customer-branch',
  tagIds: 'customer-tags',
  internalNote: 'customer-note',
};

const EMPTY_FORM: CustomerFormValue = {
  type: 'residential',
  companyName: '',
  firstName: '',
  lastName: '',
  title: '',
  email: '',
  phone: '',
  prefersEmail: true,
  prefersSms: false,
  addressLine1: '',
  city: '',
  stateRegion: '',
  postalCode: '',
  serviceInstructions: '',
  branchId: null,
  tagIds: [],
  internalNote: '',
};

const text = (value: string): string | null => (value.trim() === '' ? null : value.trim());

/**
 * New / Edit / Customer details drawer: one component, three modes. Owns validation
 * (BR-10, BR-12, BR-13), the duplicate warning and its gating (BR-15), server-error mapping and
 * the dirty-close guard (BR-20); the page owns refreshes, the tag catalog and the 401 redirect.
 */
@Component({
  selector: 'app-customer-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    Checkbox,
    InputText,
    Message,
    Select,
    Skeleton,
    SpinnerIcon,
    Textarea,
    DrawerShell,
    ErrorSummary,
    FormField,
    TagSelector,
  ],
  templateUrl: './customer-drawer.html',
  styleUrl: './customer-drawer.scss',
})
export class CustomerDrawer {
  private readonly customers = inject(CustomersService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly open = input.required<boolean>();
  readonly mode = input.required<CustomerDrawerMode>();
  readonly customerId = input<string | null>(null);
  readonly branches = input<readonly BranchOption[]>([]);
  /** Organization country (BR-10 state/ZIP rules); US when unknown. */
  readonly countryCode = input<string | null>(null);
  readonly tags = input<readonly CustomerTag[]>([]);
  readonly tagsLoading = input(false);
  /** Field to focus once the detail has loaded (the Internal notes pencil, BR-08). */
  readonly focusField = input<CustomerFieldKey | null>(null);

  readonly saved = output<void>();
  readonly closed = output<void>();
  readonly unauthorized = output<void>();
  /** The customer no longer exists (`404`). */
  readonly unavailable = output<void>();
  /** "View existing customer": the page opens this customer in the edit drawer. */
  readonly openCustomer = output<string>();
  readonly tagCreated = output<CustomerTag>();

  readonly titleId = TITLE_ID;
  readonly summaryId = SUMMARY_ID;
  readonly controlIds = CONTROL_IDS;
  readonly resolveMessage = RESOLVE_MATCH_MESSAGE;
  readonly statusLabels = STATUS_LABELS;
  readonly stateOptions = US_STATE_OPTIONS;
  readonly typeOptions: readonly { value: CustomerType; label: string; icon: string }[] = [
    { value: 'residential', label: 'Residential', icon: 'pi-home' },
    { value: 'commercial', label: 'Commercial', icon: 'pi-building' },
  ];

  readonly readOnly = computed(() => this.mode() === 'view');
  readonly isCreate = computed(() => this.mode() === 'create');
  readonly title = computed(() =>
    this.isCreate() ? 'New customer' : this.readOnly() ? 'Customer details' : 'Edit customer',
  );
  readonly usesStateList = computed(() => {
    const country = this.countryCode();
    return country === null || country === '' || country.toUpperCase() === 'US';
  });

  readonly form = signal<CustomerFormValue>(EMPTY_FORM);
  readonly fieldErrors = signal<CustomerFieldErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly detail = signal<CustomerDetail | null>(null);
  readonly detailLoading = signal(false);
  readonly detailFailed = signal(false);
  readonly creatingTag = signal(false);

  // Duplicate warning (BR-15).
  readonly matches = signal<readonly DuplicateMatch[]>([]);
  private readonly resolvedKey = signal<string | null>(null);
  private readonly valuesKey = computed(
    () => `${normalizeEmail(this.form().email) ?? ''}|${normalizePhone(this.form().phone) ?? ''}`,
  );
  readonly panelVisible = computed(
    () => !this.readOnly() && this.matches().length > 0 && this.resolvedKey() !== this.valuesKey(),
  );
  readonly viewTarget = computed(
    () => this.matches().find((match) => match.inScope && !!match.customerId) ?? null,
  );
  readonly differentFields = computed<readonly ('email' | 'phone')[]>(() => {
    const fields = new Set(this.matches().map((match) => match.matchedField));
    return (['email', 'phone'] as const).filter((field) => fields.has(field));
  });

  private readonly snapshot = signal<string | null>(null);
  private readonly currentId = computed(() => this.customerId());
  private submitAttempted = false;
  private detailRequest: Subscription | null = null;
  /** `typing` waits for 500 ms of idle; `blur` checks immediately and replaces a pending wait. */
  private readonly checkTrigger = new Subject<'typing' | 'blur'>();

  readonly branchOptions = computed<BranchOption[]>(() => {
    const options = this.branches();
    const current = this.form().branchId;
    return current !== null && current !== '' && !options.some((option) => option.id === current)
      ? [...options, { id: current, name: 'Current branch' }]
      : [...options];
  });
  readonly selectedTags = computed<readonly CustomerTag[]>(() => {
    const known = new Map<string, CustomerTag>();
    for (const tag of [...(this.detail()?.tags ?? []), ...this.tags()]) {
      known.set(tag.id, tag);
    }
    return this.form()
      .tagIds.map((id) => known.get(id))
      .filter((tag): tag is CustomerTag => tag !== undefined);
  });
  readonly catalogTags = computed<readonly CustomerTag[]>(() => {
    const catalog = new Map(this.tags().map((tag) => [tag.id, tag]));
    for (const tag of this.detail()?.tags ?? []) {
      catalog.set(tag.id, tag);
    }
    return [...catalog.values()];
  });

  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    CUSTOMER_FIELD_KEYS.filter((field) => this.fieldErrors()[field] !== undefined).map((field) => ({
      fieldId: CONTROL_IDS[field],
      label: FIELD_LABELS[field],
      message: this.fieldErrors()[field] as string,
    })),
  );

  private readonly currentKey = computed(() =>
    JSON.stringify({ ...this.form(), tagIds: [...this.form().tagIds].sort() }),
  );
  readonly dirty = computed(
    () => this.snapshot() !== null && this.snapshot() !== this.currentKey(),
  );
  readonly saveBlocked = computed(
    () => this.submitting() || this.detailLoading() || this.detailFailed() || this.panelVisible(),
  );

  constructor() {
    effect(() => {
      if (this.open()) {
        const mode = this.mode();
        const id = this.customerId();
        untracked(() => this.initialize(mode, id));
      }
    });
    this.checkTrigger
      .pipe(
        switchMap((trigger) => (trigger === 'blur' ? of(trigger) : timer(DUPLICATE_IDLE_MS))),
        switchMap(() => this.runDuplicateCheck()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        if (result.key === this.valuesKey()) {
          this.matches.set(result.matches);
        }
      });
    this.destroyRef.onDestroy(() => this.detailRequest?.unsubscribe());
  }

  error(field: CustomerFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  describedBy(field: CustomerFieldKey): string | null {
    return this.error(field) !== null ? `${CONTROL_IDS[field]}-error` : null;
  }

  matchLine(match: DuplicateMatch): string {
    return [match.primaryEmail, formatPhone(match.primaryPhone)]
      .filter((part) => !!part)
      .join(' · ');
  }

  matchMeta(match: DuplicateMatch): string {
    const status = STATUS_LABELS[match.displayStatus] ?? match.displayStatus;
    return typeof match.propertyCount === 'number'
      ? `${propertyCountText(match.propertyCount)} · ${status}`
      : status;
  }

  // Loading

  private initialize(mode: CustomerDrawerMode, id: string | null): void {
    this.detailRequest?.unsubscribe();
    this.submitAttempted = false;
    this.submitting.set(false);
    this.fieldErrors.set({});
    this.pageMessage.set(null);
    this.detail.set(null);
    this.detailFailed.set(false);
    this.matches.set([]);
    this.resolvedKey.set(null);
    const branches = this.branches();
    this.form.set({
      ...EMPTY_FORM,
      branchId: branches.length === 1 ? branches[0].id : null,
    });

    if (mode === 'create' || id === null) {
      this.detailLoading.set(false);
      this.snapshot.set(this.currentKey());
      return;
    }
    this.loadDetail(id);
  }

  loadDetail(id: string | null = this.currentId()): void {
    if (id === null) {
      return;
    }
    this.detailRequest?.unsubscribe();
    this.detailLoading.set(true);
    this.detailFailed.set(false);
    this.snapshot.set(null);
    this.detailRequest = this.customers.get(id).subscribe({
      next: (detail) => {
        this.detailLoading.set(false);
        this.detail.set(detail);
        this.form.set(this.fromDetail(detail));
        this.snapshot.set(this.currentKey());
        this.focusRequestedField();
      },
      error: (error: unknown) => {
        this.detailLoading.set(false);
        const apiError = isApiError(error) ? error : null;
        if (apiError?.kind === 'unauthorized') {
          this.unauthorized.emit();
        } else if (apiError?.kind === 'not-found') {
          this.unavailable.emit();
        } else {
          this.detailFailed.set(true);
        }
      },
    });
  }

  private focusRequestedField(): void {
    const field = this.focusField();
    if (field === null) {
      return;
    }
    afterNextRender(
      () => this.hostElement.querySelector<HTMLElement>(`#${CONTROL_IDS[field]}`)?.focus(),
      { injector: this.injector },
    );
  }

  private fromDetail(detail: CustomerDetail): CustomerFormValue {
    return {
      type: detail.type,
      companyName: detail.companyName ?? '',
      firstName: detail.contact.firstName,
      lastName: detail.contact.lastName,
      title: detail.contact.title ?? '',
      email: detail.contact.email,
      phone: formatPhone(detail.contact.phone),
      prefersEmail: detail.contact.prefersEmail,
      prefersSms: detail.contact.prefersSms,
      addressLine1: detail.property.addressLine1,
      city: detail.property.city,
      stateRegion: detail.property.stateRegion ?? '',
      postalCode: detail.property.postalCode ?? '',
      serviceInstructions: detail.serviceInstructions ?? '',
      branchId: detail.branchId,
      tagIds: detail.tags.map((tag) => tag.id),
      internalNote: detail.internalNote ?? '',
    };
  }

  // Field handling

  /** Updates a value; a field that already shows an error (or after a submit) revalidates live. */
  patch(changes: Partial<CustomerFormValue>, field?: CustomerFieldKey): void {
    const before = this.valuesKey();
    this.form.update((current) => ({ ...current, ...changes }));
    if (field !== undefined) {
      this.revalidate(field);
      if (field === 'phone' || field === 'type') {
        this.revalidate('preferences');
      }
    }
    if (this.valuesKey() !== before) {
      this.matches.set([]);
      if (!this.readOnly()) {
        this.checkTrigger.next('typing');
      }
    }
  }

  onType(type: CustomerType): void {
    const current = this.form().type;
    if (this.isCreate() || current === null || current === type) {
      this.patch({ type }, 'type');
      return;
    }
    this.confirmTypeChange(current, type);
  }

  /** BR-21: edit-mode type change asks first; nothing persists until Save changes. */
  private confirmTypeChange(current: CustomerType, next: CustomerType): void {
    const { firstName, lastName, companyName } = this.form();
    const toCommercial = next === 'commercial';
    this.confirmationService.confirm({
      header: toCommercial
        ? 'Change to a commercial customer?'
        : 'Change to a residential customer?',
      message: toCommercial
        ? `Add a company name. ${firstName.trim()} ${lastName.trim()} stays the primary contact.`
        : `${companyName.trim()} will no longer be the display name. The customer will be shown as the primary contact's name, and the company name and contact title will be removed when you save.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Change type' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => {
        this.patch(
          toCommercial ? { type: next } : { type: next, companyName: '', title: '' },
          'type',
        );
        this.setFieldError('companyName', null);
        this.setFieldError('title', null);
      },
      reject: () => this.restoreTypeRadio(current),
    });
  }

  private restoreTypeRadio(type: CustomerType): void {
    const radio = this.hostElement.querySelector<HTMLInputElement>(`#customer-type-${type}`);
    if (radio !== null) {
      radio.checked = true;
    }
  }

  onTagToggled(tag: CustomerTag): void {
    const ids = this.form().tagIds;
    this.patch(
      { tagIds: ids.includes(tag.id) ? ids.filter((id) => id !== tag.id) : [...ids, tag.id] },
      'tagIds',
    );
  }

  onTagRemoved(tag: CustomerTag): void {
    this.patch({ tagIds: this.form().tagIds.filter((id) => id !== tag.id) }, 'tagIds');
  }

  onTagCreate(name: string): void {
    if (this.readOnly() || this.creatingTag() || this.form().tagIds.length >= MAX_TAGS) {
      return;
    }
    this.creatingTag.set(true);
    this.customers
      .createTag(name)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (tag) => {
          this.creatingTag.set(false);
          this.tagCreated.emit(tag);
          if (!this.form().tagIds.includes(tag.id)) {
            this.patch({ tagIds: [...this.form().tagIds, tag.id] }, 'tagIds');
          }
        },
        error: (error: unknown) => {
          this.creatingTag.set(false);
          if (isApiError(error) && error.kind === 'unauthorized') {
            this.unauthorized.emit();
            return;
          }
          this.messageService.add({ severity: 'error', summary: TAG_FAILED_MESSAGE });
        },
      });
  }

  onFieldBlur(field: CustomerFieldKey): void {
    if (this.submitting() || this.readOnly()) {
      return;
    }
    this.setFieldError(field, this.validate(field));
    if (field === 'phone') {
      this.setFieldError('preferences', this.validate('preferences'));
    }
    if (field === 'email' || field === 'phone') {
      this.checkTrigger.next('blur');
    }
  }

  private revalidate(field: CustomerFieldKey): void {
    if (this.fieldErrors()[field] !== undefined || this.submitAttempted) {
      this.setFieldError(field, this.validate(field));
    }
  }

  private validate(field: CustomerFieldKey): string | null {
    return validateCustomerField(field, this.form(), this.usesStateList());
  }

  private setFieldError(field: CustomerFieldKey, message: string | null): void {
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

  // Duplicate warning (BR-15)

  private runDuplicateCheck() {
    const email = normalizeEmail(this.form().email);
    const phone = normalizePhone(this.form().phone);
    const key = this.valuesKey();
    if (
      !this.open() ||
      this.readOnly() ||
      this.detailLoading() ||
      (email === null && phone === null)
    ) {
      return of({ key, matches: [] as readonly DuplicateMatch[] });
    }
    const excludeId = this.isCreate() ? null : this.currentId();
    const body: DuplicateCheckRequest = {
      ...(email !== null ? { email } : {}),
      ...(phone !== null ? { phone } : {}),
      ...(excludeId !== null ? { excludeCustomerId: excludeId } : {}),
    };
    return this.customers.duplicateCheck(body).pipe(
      map((response) => ({ key, matches: response.matches })),
      // A failed check shows no panel and never blocks saving.
      catchError((error: unknown) => {
        if (isApiError(error) && error.kind === 'unauthorized') {
          this.unauthorized.emit();
        }
        return of({ key, matches: [] as readonly DuplicateMatch[] });
      }),
    );
  }

  createAnyway(): void {
    this.resolvedKey.set(this.valuesKey());
  }

  useDifferent(field: 'email' | 'phone'): void {
    this.patch(field === 'email' ? { email: '' } : { phone: '' }, field);
    this.matches.set([]);
    this.hostElement.querySelector<HTMLElement>(`#${CONTROL_IDS[field]}`)?.focus();
  }

  viewExisting(match: DuplicateMatch): void {
    const id = match.customerId;
    if (!id) {
      return;
    }
    if (this.dirty()) {
      this.confirmDiscard(() => this.openCustomer.emit(id));
      return;
    }
    this.openCustomer.emit(id);
  }

  // Submit

  submit(): void {
    if (this.saveBlocked() || this.readOnly()) {
      return;
    }
    this.submitAttempted = true;
    this.pageMessage.set(null);

    const value = this.form();
    const errors = validateCustomer(value, this.usesStateList());
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusFirstInvalid();
      return;
    }

    const commercial = value.type === 'commercial';
    const base = {
      companyName: commercial ? text(value.companyName) : null,
      contact: {
        firstName: value.firstName.trim(),
        lastName: value.lastName.trim(),
        title: commercial ? text(value.title) : null,
        email: normalizeEmail(value.email) as string,
        phone: normalizePhone(value.phone),
        prefersEmail: value.prefersEmail,
        prefersSms: value.prefersSms,
      },
      property: {
        addressLine1: value.addressLine1.trim(),
        city: value.city.trim(),
        stateRegion: text(value.stateRegion),
        postalCode: text(value.postalCode),
      },
      serviceInstructions: text(value.serviceInstructions),
      internalNote: text(value.internalNote),
      branchId: value.branchId as string,
      tagIds: value.tagIds,
    };
    const id = this.isCreate() ? null : this.currentId();
    const request$: Observable<unknown> =
      id === null
        ? this.customers.create({ ...base, type: value.type as CustomerType } as CustomerRequest)
        : this.customers.update(id, { ...base, type: value.type as CustomerType });

    this.submitting.set(true);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.submitting.set(false);
        this.messageService.add({
          severity: 'success',
          summary: id === null ? 'Customer created.' : 'Customer updated.',
        });
        this.closed.emit();
        this.saved.emit();
      },
      error: (error: unknown) => this.handleFailed(error),
    });
  }

  private handleFailed(error: unknown): void {
    this.submitting.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.unavailable.emit();
      return;
    }
    if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const mapped = mapServerFieldErrors(apiError.fieldErrors);
      if (Object.keys(mapped).length > 0) {
        this.fieldErrors.set(
          mapped.type !== undefined ? { ...mapped, type: 'Choose a customer type.' } : mapped,
        );
        this.focusFirstInvalid();
        return;
      }
    }
    this.messageService.add({ severity: 'error', summary: SAVE_FAILED_MESSAGE });
  }

  // Closing

  /** Read by the page's route-leave guard and drawer switching. */
  isDirty(): boolean {
    return this.open() && this.dirty();
  }

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.dirty() && !this.readOnly()) {
      this.confirmDiscard(() => this.closed.emit());
      return;
    }
    this.closed.emit();
  }

  private confirmDiscard(accept: () => void): void {
    this.confirmationService.confirm(
      discardChangesConfirmation({
        subject: 'this customer',
        accept,
      }),
    );
  }

  private focusFirstInvalid(): void {
    afterNextRender(
      () => {
        const target = this.hostElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        (target ?? this.hostElement.querySelector<HTMLElement>(`#${SUMMARY_ID}`))?.focus();
      },
      { injector: this.injector },
    );
  }
}
