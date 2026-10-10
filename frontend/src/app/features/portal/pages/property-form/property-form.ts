import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { map, tap } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { PortalSessionService } from '../../../../core/services/portal-session.service';
import {
  POSTAL_CODE_INVALID_MESSAGE,
  REQUIRED_MESSAGE,
  STATE_INVALID_MESSAGE,
  maxLengthMessage,
} from '../../../service-request/service-request.messages';
import { US_STATES } from '../../../service-request/service-request.validators';
import { PortalState } from '../../components/portal-state/portal-state';
import {
  GENERIC_ERROR_MESSAGE,
  PortalProperty,
  RATE_LIMITED_MESSAGE,
} from '../../models/portal.model';
import { PortalPropertiesService } from '../../services/portal-properties.service';
import { PortalResource } from '../../utils/portal-resource';

export const PROPERTY_CHANGED_MESSAGE = 'This property changed. Reload and try again.';
export const ADDRESS_LOCKED_MESSAGE = "The address can't be changed online.";
export const FIELD_FALLBACK_MESSAGE = 'Check this field and try again.';

export type PropertyField =
  | 'name'
  | 'addressLine1'
  | 'addressLine2'
  | 'city'
  | 'stateRegion'
  | 'postalCode'
  | 'accessInstructions';
type FieldErrors = Partial<Record<PropertyField, string>>;

const NOT_FOUND: ApiError = {
  kind: 'not-found',
  status: 404,
  message: 'Not found',
  fieldErrors: {},
};

const FIELD_ORDER: readonly PropertyField[] = [
  'name',
  'addressLine1',
  'addressLine2',
  'city',
  'stateRegion',
  'postalCode',
  'accessInstructions',
];

function textError(value: string, max: number, required: boolean): string | null {
  const length = value.trim().length;
  if (required && length === 0) {
    return REQUIRED_MESSAGE;
  }
  return length > max ? maxLengthMessage(max) : null;
}

/** Field rules of BR-33 (name 1–140, public BR-04 address rules, instructions ≤ 1000). */
export function validateProperty(
  values: Readonly<Record<PropertyField, string>>,
  includeAddress: boolean,
): FieldErrors {
  const checks: [PropertyField, string | null][] = [
    ['name', textError(values.name, 140, true)],
    ['accessInstructions', textError(values.accessInstructions, 1000, false)],
  ];
  if (includeAddress) {
    checks.push(
      ['addressLine1', textError(values.addressLine1, 180, true)],
      ['addressLine2', textError(values.addressLine2, 180, false)],
      ['city', textError(values.city, 100, true)],
      ['stateRegion', US_STATES.includes(values.stateRegion) ? null : STATE_INVALID_MESSAGE],
      [
        'postalCode',
        /^\d{5}(-\d{4})?$/.test(values.postalCode.trim()) ? null : POSTAL_CODE_INVALID_MESSAGE,
      ],
    );
  }
  const errors: FieldErrors = {};
  for (const [field, message] of checks) {
    if (message !== null) {
      errors[field] = message;
    }
  }
  return errors;
}

/** Add property (`/portal/properties/new`) and edit (`/portal/properties/:propertyId/edit`) forms. */
@Component({
  selector: 'app-portal-property-form',
  imports: [
    ButtonDirective,
    Checkbox,
    FormsModule,
    InputText,
    Message,
    PortalState,
    ReactiveFormsModule,
    RouterLink,
    Select,
    SpinnerIcon,
    Textarea,
  ],
  templateUrl: './property-form.html',
  styleUrls: ['../../portal.scss', '../../portal-form.scss'],
})
export class PortalPropertyForm {
  private readonly properties = inject(PortalPropertiesService);
  private readonly sessions = inject(PortalSessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly propertyId = inject(ActivatedRoute).snapshot.paramMap.get('propertyId');

  readonly editing = this.propertyId !== null;
  readonly resource = new PortalResource<PortalProperty>();
  readonly states = [...US_STATES];

  readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true }),
    addressLine1: new FormControl('', { nonNullable: true }),
    addressLine2: new FormControl('', { nonNullable: true }),
    city: new FormControl('', { nonNullable: true }),
    stateRegion: new FormControl('', { nonNullable: true }),
    postalCode: new FormControl('', { nonNullable: true }),
    accessInstructions: new FormControl('', { nonNullable: true }),
  });
  readonly makePrimary = signal(false);

  readonly errors = signal<FieldErrors>({});
  readonly failure = signal<string | null>(null);
  readonly conflict = signal(false);
  readonly submitting = signal(false);
  readonly organizationName = computed(
    () => this.sessions.session()?.account.organizationName ?? 'the company',
  );
  readonly alreadyPrimary = computed(() => this.resource.data()?.isPrimary === true);

  constructor() {
    if (this.editing) {
      this.load();
    }
  }

  /** The list endpoint is the only read of a property; edit loads it from there. */
  load(): void {
    this.conflict.set(false);
    this.failure.set(null);
    const id = this.propertyId;
    this.resource.load(
      this.properties.list().pipe(
        map((list) => {
          const property = list.find((item) => item.id === id);
          if (property === undefined) {
            throw NOT_FOUND;
          }
          return property;
        }),
        tap((property) => {
          this.form.patchValue({
            name: property.name,
            accessInstructions: property.accessInstructions ?? '',
          });
          this.makePrimary.set(property.isPrimary);
        }),
      ),
    );
  }

  submit(): void {
    if (this.submitting()) {
      return;
    }
    this.failure.set(null);
    this.conflict.set(false);
    const values = this.form.getRawValue();
    const errors = validateProperty(values, !this.editing);
    this.errors.set(errors);
    const first = FIELD_ORDER.find((field) => errors[field] !== undefined);
    if (first !== undefined) {
      document.getElementById(`portal-property-${first}`)?.focus();
      return;
    }

    this.submitting.set(true);
    const instructions = values.accessInstructions.trim();
    const current = this.resource.data();
    const request$ =
      this.editing && current !== null && this.propertyId !== null
        ? this.properties.update(this.propertyId, {
            name: values.name.trim(),
            accessInstructions: instructions === '' ? null : instructions,
            isPrimary: this.makePrimary(),
            updatedAt: current.updatedAt,
          })
        : this.properties.create({
            name: values.name.trim(),
            addressLine1: values.addressLine1.trim(),
            addressLine2: values.addressLine2.trim(),
            city: values.city.trim(),
            stateRegion: values.stateRegion,
            postalCode: values.postalCode.trim(),
            countryCode: 'US',
            accessInstructions: instructions,
          });
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => void this.router.navigateByUrl('/portal/properties'),
      error: (error: unknown) => this.failed(error),
    });
  }

  private failed(error: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.status === 409) {
      this.conflict.set(true);
      this.failure.set(PROPERTY_CHANGED_MESSAGE);
    } else if (apiError?.kind === 'validation') {
      const mapped: FieldErrors = {};
      for (const field of FIELD_ORDER) {
        if (apiError.fieldErrors[field] !== undefined) {
          mapped[field] = FIELD_FALLBACK_MESSAGE;
        }
      }
      if (Object.keys(mapped).length > 0) {
        this.errors.set(mapped);
      } else {
        this.failure.set(this.editing ? ADDRESS_LOCKED_MESSAGE : FIELD_FALLBACK_MESSAGE);
      }
    } else if (apiError?.kind === 'rate-limited') {
      this.failure.set(RATE_LIMITED_MESSAGE);
    } else {
      this.failure.set(GENERIC_ERROR_MESSAGE);
    }
  }
}
