import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { isApiError } from '../../../core/models/api-error.model';
import {
  AvailabilityData,
  ContactData,
  PropertyData,
  ServiceData,
  ServiceRequestForm,
  ServiceRequestPayload,
  StepErrors,
  WizardData,
  WizardStep,
} from '../models/service-request.model';
import {
  ATTACHMENT_SIZE_MESSAGE,
  NOT_SURE_LABEL,
  RATE_LIMITED_MESSAGE,
  SERVER_ATTACHMENTS_MESSAGE,
  SERVER_FIELD_MESSAGE,
  SUBMIT_ERROR_MESSAGE,
  TIME_WINDOW_LABELS,
  URGENCY_LABELS,
} from '../service-request.messages';
import {
  STEP_FIELDS,
  STEP_ORDER,
  fieldId,
  firstInvalidField,
  stepOfField,
  todayInTimeZone,
  validateNewFile,
  validateStep,
} from '../service-request.validators';
import { ServiceRequestService } from './service-request.service';

type Section = 'contact' | 'property' | 'service' | 'availability';

export interface SummarySection {
  readonly step: Section;
  readonly title: string;
  /** Empty while the section has not been completed. */
  readonly lines: readonly string[];
}

export interface FocusRequest {
  /** `'heading'` or a field path. */
  readonly target: string;
  readonly seq: number;
}

export interface SubmissionResult {
  readonly requestNumber: string;
  readonly firstName: string;
  readonly email: string;
  /** Snapshot taken before the wizard data is cleared, shown on the confirmation page. */
  readonly submitted: {
    readonly contact: readonly string[];
    readonly property: readonly string[];
    readonly service: string;
    readonly preferredTime: string;
  };
}

const ALL_FIELDS = Object.values(STEP_FIELDS).flat();

function initialData(): WizardData {
  return {
    contact: {
      firstName: '',
      lastName: '',
      email: '',
      phone: '',
      prefersEmail: true,
      prefersSms: false,
    },
    property: {
      propertyType: 'home',
      addressLine1: '',
      addressLine2: '',
      city: '',
      state: '',
      postalCode: '',
      accessInstructions: '',
    },
    service: {
      categoryId: '',
      serviceId: '',
      notSure: false,
      description: '',
      urgency: 'standard',
      hasActiveDamage: false,
    },
    availability: {
      dateMode: 'asap',
      preferredDate: '',
      timeWindow: 'morning',
      schedulingNotes: '',
    },
  };
}

/**
 * In-memory wizard state (BR-20: no browser storage). Provided by the page, so
 * leaving the page discards every entered value.
 */
@Injectable()
export class ServiceRequestWizardStore {
  private readonly api = inject(ServiceRequestService);
  private readonly destroyRef = inject(DestroyRef);

  private slug = '';
  private attempted = new Set<WizardStep>();
  private focusSeq = 0;

  readonly config = signal<ServiceRequestForm | null>(null);
  readonly step = signal<WizardStep>('contact');
  readonly data = signal<WizardData>(initialData());
  readonly files = signal<readonly File[]>([]);
  readonly fileMessages = signal<readonly string[]>([]);
  readonly consent = signal(false);
  /** Honeypot (BR-17): bound to a hidden input and never filled by a person. */
  readonly website = signal('');
  readonly completed = signal<ReadonlySet<WizardStep>>(new Set());
  readonly stepErrors = signal<StepErrors>({});
  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly result = signal<SubmissionResult | null>(null);
  readonly focusRequest = signal<FocusRequest | null>(null);

  readonly stepIndex = computed(() => STEP_ORDER.indexOf(this.step()));
  readonly categoryServices = computed(() => [
    ...(this.config()?.categories.find((c) => c.id === this.data().service.categoryId)?.services ??
      []),
  ]);
  readonly attachmentSummary = computed(() => {
    const count = this.files().length;
    return count === 0 ? null : `${count} ${count === 1 ? 'attachment' : 'attachments'}`;
  });

  readonly summary = computed<readonly SummarySection[]>(() => {
    const { contact, property, service, availability } = this.data();
    const done = this.completed();
    const categoryName = this.config()?.categories.find((c) => c.id === service.categoryId)?.name;
    const serviceName = service.notSure
      ? NOT_SURE_LABEL
      : this.categoryServices().find((s) => s.id === service.serviceId)?.name;
    const when =
      availability.dateMode === 'asap'
        ? 'As soon as possible'
        : availability.dateMode === 'flexible'
          ? "I'm flexible"
          : availability.preferredDate;

    const sections: readonly SummarySection[] = [
      {
        step: 'contact',
        title: 'Contact',
        lines: [`${contact.firstName.trim()} ${contact.lastName.trim()}`, contact.email.trim()],
      },
      {
        step: 'property',
        title: 'Property',
        lines: [
          property.propertyType === 'home' ? 'Home' : 'Business',
          [property.addressLine1.trim(), property.addressLine2.trim()].filter(Boolean).join(', '),
          `${property.city.trim()}, ${property.state} ${property.postalCode.trim()}`,
        ],
      },
      {
        step: 'service',
        title: 'Service details',
        lines: [
          [categoryName, serviceName].filter(Boolean).join(' · '),
          URGENCY_LABELS[service.urgency],
        ],
      },
      {
        step: 'availability',
        title: 'Availability',
        lines: [when, TIME_WINDOW_LABELS[availability.timeWindow]],
      },
    ];
    return sections.map((s) => (done.has(s.step) ? s : { ...s, lines: [] }));
  });

  /** Organization website as an absolute URL, or `null` when none is set. */
  readonly homeUrl = computed(() => {
    const website = this.config()?.website?.trim() ?? '';
    if (website === '') return null;
    return /^https?:\/\//i.test(website) ? website : `https://${website}`;
  });

  error(path: string): string | null {
    return this.stepErrors()[path] ?? null;
  }

  /** `aria-describedby` target for a field that currently shows an error. */
  errorId(path: string): string | null {
    return this.error(path) === null ? null : `${fieldId(path)}-error`;
  }

  configure(slug: string, config: ServiceRequestForm): void {
    this.slug = slug;
    this.config.set(config);
  }

  /** Organization-local today (`YYYY-MM-DD`), used for the date limits. */
  today(): string {
    return todayInTimeZone(this.config()?.timezone);
  }

  patchContact(patch: Partial<ContactData>): void {
    this.data.update((d) => ({ ...d, contact: { ...d.contact, ...patch } }));
  }

  patchProperty(patch: Partial<PropertyData>): void {
    this.data.update((d) => ({ ...d, property: { ...d.property, ...patch } }));
  }

  patchService(patch: Partial<ServiceData>): void {
    this.data.update((d) => {
      const next = { ...d.service, ...patch };
      // A different category invalidates the chosen service; "not sure" clears it (FR-04).
      if (
        patch.serviceId === undefined &&
        patch.categoryId !== undefined &&
        patch.categoryId !== d.service.categoryId
      ) {
        next.serviceId = '';
      }
      if (next.notSure) {
        next.serviceId = '';
      }
      return { ...d, service: next };
    });
  }

  patchAvailability(patch: Partial<AvailabilityData>): void {
    this.data.update((d) => ({ ...d, availability: { ...d.availability, ...patch } }));
  }

  setConsent(value: boolean): void {
    this.consent.set(value);
  }

  addFiles(candidates: readonly File[]): void {
    const accepted = [...this.files()];
    const messages: string[] = [];
    for (const file of candidates) {
      const reason = validateNewFile(accepted, file);
      if (reason === null) {
        accepted.push(file);
      } else {
        messages.push(reason);
      }
    }
    this.files.set(accepted);
    this.fileMessages.set(messages);
    this.clearError('attachments');
  }

  removeFile(index: number): void {
    this.files.update((files) => files.filter((_, i) => i !== index));
    this.fileMessages.set([]);
    this.clearError('attachments');
  }

  /** Re-checks one field after blur/change, only once its step failed an attempt. */
  revalidate(path: string): void {
    const step = stepOfField(path);
    if (step === null || !this.attempted.has(step)) return;
    const message = this.validate(step)[path];
    if (message === undefined) {
      this.clearError(path);
    } else {
      this.stepErrors.update((errors) => ({ ...errors, [path]: message }));
    }
  }

  /** Validates the current step; advances when valid. Returns whether it advanced. */
  next(): boolean {
    const step = this.step();
    const errors = this.validate(step);
    this.attempted.add(step);
    this.setStepErrors(step, errors);

    const first = firstInvalidField(step, errors);
    if (first !== null) {
      this.requestFocus(first);
      return false;
    }

    this.completed.update((done) => new Set(done).add(step));
    const target = STEP_ORDER[this.stepIndex() + 1];
    if (target !== undefined) {
      this.goTo(target);
    }
    return true;
  }

  back(): void {
    const target = STEP_ORDER[this.stepIndex() - 1];
    if (target !== undefined) {
      this.goTo(target);
    }
  }

  /** Opens a completed step with its data intact (summary panel and Review Edit links). */
  edit(step: WizardStep): void {
    if (this.completed().has(step)) {
      this.goTo(step);
    }
  }

  /** Validates everything, then sends once; ignored while a request is in flight. */
  submit(): void {
    if (this.submitting()) return;
    this.submitError.set(null);

    for (const step of STEP_ORDER) {
      const errors = this.validate(step);
      const first = firstInvalidField(step, errors);
      if (first !== null) {
        this.attempted.add(step);
        this.setStepErrors(step, errors);
        if (step !== this.step()) {
          this.step.set(step);
        }
        this.requestFocus(first);
        return;
      }
    }

    this.submitting.set(true);
    this.api
      .submit(this.slug, this.buildPayload(), this.files())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          const { firstName, email } = this.data().contact;
          const submitted = this.snapshot();
          this.result.set({
            requestNumber: created.requestNumber,
            firstName: firstName.trim(),
            email: email.trim(),
            submitted,
          });
          this.submitting.set(false);
          this.clearData();
          this.requestFocus('heading');
        },
        error: (failure: unknown) => this.handleError(failure),
      });
  }

  /** "Submit another request": empty wizard at step 1. */
  reset(): void {
    this.clearData();
    this.result.set(null);
    this.submitError.set(null);
    this.step.set('contact');
    this.requestFocus('heading');
  }

  private snapshot(): SubmissionResult['submitted'] {
    const sections = this.summary();
    const lines = (step: Section): readonly string[] =>
      sections.find((s) => s.step === step)?.lines ?? [];
    const { service, availability } = this.data();
    const categoryName = this.config()?.categories.find((c) => c.id === service.categoryId)?.name;
    return {
      contact: lines('contact'),
      // The first property line is the type (Home/Business); the address follows.
      property: lines('property').slice(1),
      service: [categoryName, URGENCY_LABELS[service.urgency]].filter(Boolean).join(' · '),
      preferredTime: [
        lines('availability')[0],
        TIME_WINDOW_LABELS[availability.timeWindow].split(' (')[0],
      ]
        .filter(Boolean)
        .join(' · '),
    };
  }

  private clearData(): void {
    this.data.set(initialData());
    this.files.set([]);
    this.fileMessages.set([]);
    this.consent.set(false);
    this.website.set('');
    this.completed.set(new Set());
    this.stepErrors.set({});
    this.attempted = new Set();
  }

  private validate(step: WizardStep): StepErrors {
    return validateStep(step, this.data(), {
      files: this.files(),
      today: this.today(),
      consent: this.consent(),
    });
  }

  private goTo(step: WizardStep): void {
    this.step.set(step);
    this.requestFocus('heading');
  }

  private requestFocus(target: string): void {
    this.focusRequest.set({ target, seq: ++this.focusSeq });
  }

  private clearError(path: string): void {
    this.stepErrors.update((errors) => {
      if (errors[path] === undefined) return errors;
      return Object.fromEntries(Object.entries(errors).filter(([key]) => key !== path));
    });
  }

  /** Replaces the errors of one step, leaving other steps' errors untouched. */
  private setStepErrors(step: WizardStep, errors: StepErrors): void {
    this.stepErrors.update((current) => {
      const kept = Object.fromEntries(
        Object.entries(current).filter(([path]) => stepOfField(path) !== step),
      );
      return { ...kept, ...errors };
    });
  }

  private handleError(failure: unknown): void {
    this.submitting.set(false);
    const error = isApiError(failure) ? failure : null;

    if (error?.kind === 'validation' && this.applyServerErrors(error.fieldErrors)) {
      return;
    }
    if (error?.kind === 'bad-request' && error.status === 413) {
      this.stepErrors.update((errors) => ({ ...errors, attachments: ATTACHMENT_SIZE_MESSAGE }));
      this.attempted.add('service');
      this.goTo('service');
      this.requestFocus('attachments');
      return;
    }
    this.submitError.set(
      error?.kind === 'rate-limited' ? RATE_LIMITED_MESSAGE : SUBMIT_ERROR_MESSAGE,
    );
  }

  /** Maps server field keys to fixed client messages; backend text is never shown. */
  private applyServerErrors(fieldErrors: Readonly<Record<string, readonly string[]>>): boolean {
    const mapped: Record<string, string> = {};
    for (const key of Object.keys(fieldErrors)) {
      const step = stepOfField(key);
      if (step === null) continue;
      const lower = key.toLowerCase();
      const isAttachment = lower.startsWith('attachments');
      const path = isAttachment
        ? 'attachments'
        : (ALL_FIELDS.find((field) => field.toLowerCase() === lower) ??
          (lower === 'contact.prefersemail' || lower === 'contact.preferssms'
            ? 'contact.prefersEmail'
            : STEP_FIELDS[step][0]));
      mapped[path] = isAttachment ? SERVER_ATTACHMENTS_MESSAGE : SERVER_FIELD_MESSAGE;
    }

    const steps = STEP_ORDER.filter((s) => Object.keys(mapped).some((p) => stepOfField(p) === s));
    const target = steps[0];
    if (target === undefined) return false;

    this.stepErrors.set(mapped);
    for (const step of steps) this.attempted.add(step);
    this.step.set(target);
    this.requestFocus(firstInvalidField(target, mapped) ?? 'heading');
    return true;
  }

  private buildPayload(): ServiceRequestPayload {
    const { contact, property, service, availability } = this.data();
    const { serviceId, ...serviceRest } = service;
    const { preferredDate, ...availabilityRest } = availability;
    return {
      contact: {
        ...contact,
        firstName: contact.firstName.trim(),
        lastName: contact.lastName.trim(),
        email: contact.email.trim(),
        // Phone is sent exactly as typed.
      },
      property: {
        ...property,
        addressLine1: property.addressLine1.trim(),
        addressLine2: property.addressLine2.trim(),
        city: property.city.trim(),
        postalCode: property.postalCode.trim(),
        accessInstructions: property.accessInstructions.trim(),
      },
      service: {
        ...serviceRest,
        description: service.description.trim(),
        ...(service.notSure ? {} : { serviceId }),
      },
      availability: {
        ...availabilityRest,
        schedulingNotes: availability.schedulingNotes.trim(),
        ...(availability.dateMode === 'date' ? { preferredDate } : {}),
      },
      consent: this.consent(),
      website: this.website(),
    };
  }
}
