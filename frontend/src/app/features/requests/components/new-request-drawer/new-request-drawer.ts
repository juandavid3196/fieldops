import {
  Component,
  DestroyRef,
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
import { AutoComplete } from 'primeng/autocomplete';
import { ButtonDirective } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { Subject, Subscription, catchError, of, switchMap } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { FormField } from '../../../organizations/components/form-field/form-field';
import {
  CATEGORY_REQUIRED_MESSAGE,
  DESCRIPTION_REQUIRED_MESSAGE,
  REQUIRED_MESSAGE,
  SERVICE_REQUIRED_MESSAGE,
  maxLengthMessage,
} from '../../../service-request/service-request.messages';
import {
  todayInTimeZone,
  validateAvailability,
} from '../../../service-request/service-request.validators';
import {
  CreateRequestBody,
  CustomerOption,
  DateMode,
  RequestDetail,
  RequestOptions,
  TIME_WINDOW_LABELS,
  TimeWindow,
  URGENCY_LABELS,
  Urgency,
} from '../../models/requests.model';
import { RequestsService } from '../../services/requests.service';

export const CREATE_FAILED_MESSAGE = "We couldn't save this request. Please try again.";
export const NO_CUSTOMERS_MESSAGE = 'No customers found.';

interface Choice<T extends string = string> {
  readonly code: T;
  readonly label: string;
}

/** Server field keys (`errors.<name>` or `errors.availability.<name>`) to this form's keys. */
function fieldKey(path: string): string {
  const last = (path.split('.').pop() ?? path).replace(/\[\d+\]$/, '');
  return last.charAt(0).toLowerCase() + last.slice(1);
}

/**
 * New request drawer (BR-18, FR-15): an existing in-scope customer with its contact and property,
 * plus the service details and availability. Owns validation, server `400` mapping and the
 * dirty-close guard; the page owns navigation to the created request and the refreshes.
 */
@Component({
  selector: 'app-new-request-drawer',
  imports: [
    FormsModule,
    AutoComplete,
    ButtonDirective,
    Checkbox,
    InputText,
    Select,
    SpinnerIcon,
    Textarea,
    DrawerShell,
    FormField,
  ],
  templateUrl: './new-request-drawer.html',
  styleUrl: './new-request-drawer.scss',
})
export class NewRequestDrawer {
  private readonly requests = inject(RequestsService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly open = input.required<boolean>();
  readonly options = input<RequestOptions | null>(null);
  readonly optionsLoading = input(false);

  readonly created = output<RequestDetail>();
  readonly closed = output<void>();
  readonly unauthorized = output<void>();

  readonly titleId = 'new-request-title';
  readonly noCustomers = NO_CUSTOMERS_MESSAGE;
  readonly urgencyChoices: Choice<Urgency>[] = (Object.keys(URGENCY_LABELS) as Urgency[]).map(
    (code) => ({ code, label: URGENCY_LABELS[code] }),
  );
  readonly dateModes: Choice<DateMode>[] = [
    { code: 'asap', label: 'As soon as possible' },
    { code: 'date', label: 'Choose a date' },
    { code: 'flexible', label: 'Flexible' },
  ];
  readonly windowChoices: Choice<TimeWindow>[] = (
    Object.keys(TIME_WINDOW_LABELS) as TimeWindow[]
  ).map((code) => ({ code, label: TIME_WINDOW_LABELS[code] }));

  /** Customer picker: the typed text, or the chosen customer. */
  readonly customerInput = signal<CustomerOption | string>('');
  readonly customer = signal<CustomerOption | null>(null);
  readonly suggestions = signal<CustomerOption[]>([]);
  readonly searching = signal(false);
  readonly contactId = signal('');
  readonly propertyId = signal('');
  readonly categoryId = signal('');
  readonly serviceId = signal('');
  readonly notSure = signal(false);
  readonly description = signal('');
  readonly urgency = signal<Urgency>('standard');
  readonly hasActiveDamage = signal(false);
  readonly dateMode = signal<DateMode>('asap');
  readonly preferredDate = signal('');
  readonly timeWindow = signal<TimeWindow>('any');
  readonly schedulingNotes = signal('');

  readonly errors = signal<Readonly<Record<string, string>>>({});
  readonly submitting = signal(false);

  private readonly search = new Subject<string>();
  private submit$: Subscription | null = null;
  private snapshot = '';

  readonly contactChoices = computed<Choice[]>(() =>
    (this.customer()?.contacts ?? []).map((contact) => ({
      code: contact.id,
      label: contact.name,
    })),
  );
  readonly propertyChoices = computed<Choice[]>(() =>
    (this.customer()?.properties ?? []).map((property) => ({
      code: property.id,
      label: [property.name, property.addressLine1, property.city]
        .filter((part) => !!part)
        .join(' · '),
    })),
  );
  readonly categoryChoices = computed<Choice[]>(() =>
    (this.options()?.categories ?? []).map((category) => ({
      code: category.id,
      label: category.name,
    })),
  );
  readonly serviceChoices = computed<Choice[]>(
    () =>
      this.options()
        ?.categories.find((category) => category.id === this.categoryId())
        ?.services.map((service) => ({ code: service.id, label: service.name })) ?? [],
  );
  readonly timezone = computed(() => this.options()?.timezone);

  private readonly formKey = computed(() =>
    JSON.stringify([
      this.customer()?.id ?? '',
      this.contactId(),
      this.propertyId(),
      this.categoryId(),
      this.serviceId(),
      this.notSure(),
      this.description(),
      this.urgency(),
      this.hasActiveDamage(),
      this.dateMode(),
      this.preferredDate(),
      this.timeWindow(),
      this.schedulingNotes(),
    ]),
  );
  readonly dirty = computed(() => this.formKey() !== this.snapshot);

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => this.reset());
      }
    });
    this.search
      .pipe(
        switchMap((term) => {
          this.searching.set(true);
          return this.requests.customerOptions(term).pipe(
            catchError((error: unknown) => {
              if (isApiError(error) && error.kind === 'unauthorized') {
                this.unauthorized.emit();
              }
              return of({ items: [] as readonly CustomerOption[] });
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((response) => {
        this.searching.set(false);
        this.suggestions.set([...response.items]);
      });
    this.destroyRef.onDestroy(() => this.submit$?.unsubscribe());
  }

  private reset(): void {
    this.submit$?.unsubscribe();
    this.submitting.set(false);
    this.errors.set({});
    this.customerInput.set('');
    this.customer.set(null);
    this.suggestions.set([]);
    this.contactId.set('');
    this.propertyId.set('');
    this.categoryId.set('');
    this.serviceId.set('');
    this.notSure.set(false);
    this.description.set('');
    this.urgency.set('standard');
    this.hasActiveDamage.set(false);
    this.dateMode.set('asap');
    this.preferredDate.set('');
    this.timeWindow.set('any');
    this.schedulingNotes.set('');
    this.snapshot = this.formKey();
  }

  error(field: string): string | null {
    return this.errors()[field] ?? null;
  }

  describedBy(field: string): string | null {
    return this.error(field) === null ? null : `new-request-${field}-error`;
  }

  onSearch(query: string): void {
    this.search.next(query);
  }

  /** Picking a customer defaults its primary contact and property (BR-18). */
  onCustomerInput(value: CustomerOption | string): void {
    this.customerInput.set(value);
    if (typeof value === 'string') {
      this.customer.set(null);
      this.contactId.set('');
      this.propertyId.set('');
      return;
    }
    this.customer.set(value);
    this.contactId.set((value.contacts.find((c) => c.isPrimary) ?? value.contacts[0])?.id ?? '');
    this.propertyId.set(
      (value.properties.find((p) => p.isPrimary) ?? value.properties[0])?.id ?? '',
    );
    this.clear('customerId', 'contactId', 'propertyId');
  }

  onCategory(id: string): void {
    this.categoryId.set(id);
    this.serviceId.set('');
    this.clear('categoryId', 'serviceId');
  }

  onNotSure(value: boolean): void {
    this.notSure.set(value);
    if (value) {
      this.serviceId.set('');
    }
    this.clear('serviceId');
  }

  clear(...fields: string[]): void {
    if (fields.some((field) => this.errors()[field] !== undefined)) {
      this.errors.update((current) =>
        Object.fromEntries(Object.entries(current).filter(([key]) => !fields.includes(key))),
      );
    }
  }

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.dirty()) {
      this.confirmationService.confirm(
        discardChangesConfirmation({ subject: 'this request', accept: () => this.closed.emit() }),
      );
      return;
    }
    this.closed.emit();
  }

  private validate(): Record<string, string> {
    const errors: Record<string, string> = {};
    if (this.customer() === null) {
      errors['customerId'] = REQUIRED_MESSAGE;
    }
    if (this.contactId() === '') {
      errors['contactId'] = REQUIRED_MESSAGE;
    }
    if (this.propertyId() === '') {
      errors['propertyId'] = REQUIRED_MESSAGE;
    }
    if (this.categoryId() === '') {
      errors['categoryId'] = CATEGORY_REQUIRED_MESSAGE;
    }
    if (!this.notSure() && this.serviceId() === '') {
      errors['serviceId'] = SERVICE_REQUIRED_MESSAGE;
    }
    const description = this.description().trim();
    if (description.length === 0) {
      errors['description'] = DESCRIPTION_REQUIRED_MESSAGE;
    } else if (description.length > 1000) {
      errors['description'] = maxLengthMessage(1000);
    }
    const availability = validateAvailability(
      {
        dateMode: this.dateMode(),
        preferredDate: this.preferredDate(),
        timeWindow: this.timeWindow(),
        schedulingNotes: this.schedulingNotes(),
      },
      todayInTimeZone(this.timezone()),
    );
    if (availability['availability.preferredDate'] !== undefined) {
      errors['preferredDate'] = availability['availability.preferredDate'];
    }
    if (availability['availability.schedulingNotes'] !== undefined) {
      errors['schedulingNotes'] = availability['availability.schedulingNotes'];
    }
    return errors;
  }

  save(): void {
    if (this.submitting()) {
      return;
    }
    const errors = this.validate();
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }
    const body: CreateRequestBody = {
      customerId: this.customer()!.id,
      contactId: this.contactId(),
      propertyId: this.propertyId(),
      categoryId: this.categoryId(),
      serviceId: this.notSure() ? null : this.serviceId(),
      notSure: this.notSure(),
      description: this.description().trim(),
      urgency: this.urgency(),
      hasActiveDamage: this.hasActiveDamage(),
      availability: {
        dateMode: this.dateMode(),
        preferredDate: this.dateMode() === 'date' ? this.preferredDate() : null,
        timeWindow: this.timeWindow(),
        schedulingNotes: this.schedulingNotes().trim() || null,
      },
    };
    this.submitting.set(true);
    this.submit$ = this.requests.create(body).subscribe({
      next: (detail) => {
        this.submitting.set(false);
        this.created.emit(detail);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        const apiError = isApiError(error) ? error : null;
        if (apiError?.kind === 'unauthorized') {
          this.unauthorized.emit();
          return;
        }
        const entries = Object.entries(apiError?.fieldErrors ?? {});
        if (apiError?.kind === 'validation' && entries.length > 0) {
          this.errors.set(
            Object.fromEntries(entries.map(([path, messages]) => [fieldKey(path), messages[0]])),
          );
          return;
        }
        this.messageService.add({ severity: 'error', summary: CREATE_FAILED_MESSAGE });
      },
    });
  }
}
