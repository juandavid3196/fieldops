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
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';
import { Observable, Subscription } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import {
  ErrorSummary,
  FieldErrorLink,
} from '../../../organizations/components/error-summary/error-summary';
import { FormField } from '../../../organizations/components/form-field/form-field';
import { US_STATE_OPTIONS } from '../../../organizations/data/us-states';
import { PropertyItem, PropertyRequest } from '../../models/customer.model';
import { CustomersService } from '../../services/customers.service';
import { firstLine, formatDateTime } from '../../utils/customer-detail-format';
import { BranchOption } from '../customer-drawer/customer-drawer';
import {
  PROPERTY_FIELD_KEYS,
  PROPERTY_FIELD_LABELS,
  PropertyFieldErrors,
  PropertyFieldKey,
  PropertyFormValue,
  mapPropertyServerErrors,
  validateProperty,
  validatePropertyField,
} from './property-drawer.validators';

export type PropertyDrawerMode = 'create' | 'edit' | 'view';

export const PROPERTY_SAVE_FAILED_MESSAGE = "We couldn't save the property. Try again.";
export const PROPERTY_UNAVAILABLE_MESSAGE = 'This property is no longer available.';
export const PROPERTY_ARCHIVED_EDIT_MESSAGE = 'Reactivate this property to edit it.';
const TITLE_ID = 'property-drawer-title';
const SUMMARY_ID = 'property-drawer-error-summary';
const CONTROL_IDS: Readonly<Record<PropertyFieldKey, string>> = {
  name: 'property-name',
  addressLine1: 'property-address-1',
  addressLine2: 'property-address-2',
  city: 'property-city',
  stateRegion: 'property-state',
  postalCode: 'property-zip',
  branchId: 'property-branch',
  serviceInstructions: 'property-instructions',
};
const EMPTY_FORM: PropertyFormValue = {
  name: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  stateRegion: '',
  postalCode: '',
  branchId: null,
  serviceInstructions: '',
};

const text = (value: string): string | null => (value.trim() === '' ? null : value.trim());

/**
 * Add / Edit / View property drawer (BR-05, BR-06, BR-22). Owns validation, branch option
 * handling, server-error mapping and the dirty-close guard; the page owns refreshes, toasts for
 * missing records and the 401 redirect.
 */
@Component({
  selector: 'app-property-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    InputText,
    Message,
    Select,
    Skeleton,
    SpinnerIcon,
    Textarea,
    DrawerShell,
    ErrorSummary,
    FormField,
  ],
  templateUrl: './property-drawer.html',
  styleUrl: './property-drawer.scss',
})
export class PropertyDrawer {
  private readonly customers = inject(CustomersService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly open = input.required<boolean>();
  readonly mode = input.required<PropertyDrawerMode>();
  readonly customerId = input.required<string>();
  readonly propertyId = input<string | null>(null);
  readonly branches = input<readonly BranchOption[]>([]);
  readonly countryCode = input<string | null>(null);
  readonly timezone = input('UTC');

  readonly saved = output<void>();
  readonly closed = output<void>();
  readonly unauthorized = output<void>();
  /** `404`: the property (edit/view) or the customer (create) no longer exists. */
  readonly unavailable = output<void>();
  /** `409` on save: the page refreshes its list. */
  readonly stale = output<void>();

  readonly titleId = TITLE_ID;
  readonly summaryId = SUMMARY_ID;
  readonly controlIds = CONTROL_IDS;
  readonly stateOptions = US_STATE_OPTIONS;

  readonly readOnly = computed(() => this.mode() === 'view');
  readonly isCreate = computed(() => this.mode() === 'create');
  readonly title = computed(() =>
    this.isCreate() ? 'Add property' : this.readOnly() ? 'Property details' : 'Edit property',
  );
  readonly usesStateList = computed(() => {
    const country = this.countryCode();
    return country === null || country === '' || country.toUpperCase() === 'US';
  });

  readonly form = signal<PropertyFormValue>(EMPTY_FORM);
  readonly fieldErrors = signal<PropertyFieldErrors>({});
  readonly submitting = signal(false);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly loaded = signal<PropertyItem | null>(null);

  private readonly snapshot = signal<string | null>(null);
  private submitAttempted = false;
  private request: Subscription | null = null;

  /** BR-06: a current branch outside the options is shown as the selected option with its name. */
  readonly branchOptions = computed<BranchOption[]>(() => {
    const options = this.branches();
    const current = this.loaded()?.branch;
    return current && !options.some((option) => option.id === current.id)
      ? [...options, { id: current.id, name: current.name }]
      : [...options];
  });
  readonly lastService = computed(() => {
    const last = this.loaded()?.lastService;
    if (!last) {
      return 'None';
    }
    const summary = firstLine(last.summary);
    const date = formatDateTime(last.completedAt, this.timezone());
    return summary ? `${date} · ${summary}` : date;
  });
  readonly nextAppointment = computed(() => {
    const next = this.loaded()?.nextAppointment;
    return next ? formatDateTime(next.startsAt, this.timezone()) : 'None';
  });
  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    PROPERTY_FIELD_KEYS.filter((field) => this.fieldErrors()[field] !== undefined).map((field) => ({
      fieldId: CONTROL_IDS[field],
      label: PROPERTY_FIELD_LABELS[field],
      message: this.fieldErrors()[field] as string,
    })),
  );
  private readonly currentKey = computed(() => JSON.stringify(this.form()));
  readonly dirty = computed(
    () => this.snapshot() !== null && this.snapshot() !== this.currentKey(),
  );
  readonly saveBlocked = computed(() => this.submitting() || this.loading() || this.loadFailed());

  constructor() {
    effect(() => {
      if (this.open()) {
        const mode = this.mode();
        const propertyId = this.propertyId();
        const customerId = this.customerId();
        untracked(() => this.initialize(mode, customerId, propertyId));
      }
    });
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
  }

  error(field: PropertyFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  describedBy(field: PropertyFieldKey): string | null {
    return this.error(field) !== null ? `${CONTROL_IDS[field]}-error` : null;
  }

  // Loading

  private initialize(mode: PropertyDrawerMode, customerId: string, propertyId: string | null) {
    this.submitAttempted = false;
    this.submitting.set(false);
    this.fieldErrors.set({});
    this.loaded.set(null);
    this.form.set(EMPTY_FORM);
    this.load(mode, customerId, propertyId);
  }

  load(
    mode: PropertyDrawerMode = this.mode(),
    customerId: string = this.customerId(),
    propertyId: string | null = this.propertyId(),
  ): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.loadFailed.set(false);
    this.snapshot.set(null);
    if (mode === 'create' || propertyId === null) {
      // New property default: the customer's branch when it is among the options.
      this.request = this.customers.get(customerId).subscribe({
        next: (customer) => {
          const known = this.branches().some((branch) => branch.id === customer.branchId);
          this.startForm({ ...EMPTY_FORM, branchId: known ? customer.branchId : null });
        },
        error: (error: unknown) => {
          const kind = isApiError(error) ? error.kind : null;
          if (kind === 'unauthorized') {
            this.loading.set(false);
            this.unauthorized.emit();
          } else if (kind === 'not-found') {
            this.loading.set(false);
            this.unavailable.emit();
          } else {
            this.startForm(EMPTY_FORM);
          }
        },
      });
      return;
    }
    this.request = this.customers.property(customerId, propertyId).subscribe({
      next: (property) => {
        this.loaded.set(property);
        this.startForm({
          name: property.name,
          addressLine1: property.addressLine1,
          addressLine2: property.addressLine2 ?? '',
          city: property.city,
          stateRegion: property.stateRegion ?? '',
          postalCode: property.postalCode ?? '',
          branchId: property.branch?.id ?? null,
          serviceInstructions: property.serviceInstructions ?? '',
        });
      },
      error: (error: unknown) => {
        this.loading.set(false);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.unauthorized.emit();
        } else if (kind === 'not-found') {
          this.unavailable.emit();
        } else {
          this.loadFailed.set(true);
        }
      },
    });
  }

  private startForm(value: PropertyFormValue): void {
    this.loading.set(false);
    this.form.set(value);
    this.snapshot.set(this.currentKey());
  }

  // Fields

  patch(changes: Partial<PropertyFormValue>, field: PropertyFieldKey): void {
    this.form.update((current) => ({ ...current, ...changes }));
    if (this.fieldErrors()[field] !== undefined || this.submitAttempted) {
      this.setFieldError(field, this.validate(field));
    }
  }

  onFieldBlur(field: PropertyFieldKey): void {
    if (this.submitting() || this.readOnly()) {
      return;
    }
    this.setFieldError(field, this.validate(field));
  }

  private validate(field: PropertyFieldKey): string | null {
    return validatePropertyField(field, this.form(), this.usesStateList());
  }

  private setFieldError(field: PropertyFieldKey, message: string | null): void {
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

  // Submit

  submit(): void {
    if (this.saveBlocked() || this.readOnly()) {
      return;
    }
    this.submitAttempted = true;
    const value = this.form();
    const errors = validateProperty(value, this.usesStateList());
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusFirstInvalid();
      return;
    }
    const body: PropertyRequest = {
      name: value.name.trim(),
      addressLine1: value.addressLine1.trim(),
      addressLine2: text(value.addressLine2),
      city: value.city.trim(),
      stateRegion: text(value.stateRegion),
      postalCode: text(value.postalCode),
      branchId: value.branchId as string,
      serviceInstructions: text(value.serviceInstructions),
    };
    const propertyId = this.isCreate() ? null : this.propertyId();
    const request$: Observable<unknown> =
      propertyId === null
        ? this.customers.createProperty(this.customerId(), body)
        : this.customers.updateProperty(this.customerId(), propertyId, body);

    this.submitting.set(true);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.submitting.set(false);
        this.messageService.add({
          severity: 'success',
          summary: propertyId === null ? 'Property added.' : 'Property updated.',
        });
        this.closed.emit();
        this.saved.emit();
      },
      error: (error: unknown) => this.handleFailed(error),
    });
  }

  private handleFailed(error: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.unavailable.emit();
      return;
    }
    if (apiError?.kind === 'conflict') {
      this.messageService.add({ severity: 'error', summary: PROPERTY_ARCHIVED_EDIT_MESSAGE });
      this.stale.emit();
      return;
    }
    if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const mapped = mapPropertyServerErrors(apiError.fieldErrors);
      if (Object.keys(mapped).length > 0) {
        this.fieldErrors.set(mapped);
        this.focusFirstInvalid();
        return;
      }
    }
    this.messageService.add({ severity: 'error', summary: PROPERTY_SAVE_FAILED_MESSAGE });
  }

  // Closing

  /** Read by the page's route-leave guard and drawer switching. */
  isDirty(): boolean {
    return this.open() && this.dirty() && !this.readOnly();
  }

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.isDirty()) {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this property',
          accept: () => this.closed.emit(),
        }),
      );
      return;
    }
    this.closed.emit();
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
