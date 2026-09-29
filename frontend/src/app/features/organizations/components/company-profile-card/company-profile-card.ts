import { Component, input, output } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { startWith, switchMap } from 'rxjs';

import { SelectOption } from '../../data/display-names';
import { US_STATE_OPTIONS } from '../../data/us-states';
import {
  CompanySetupFormControls,
  OrganizationFieldErrors,
  OrganizationFieldKey,
  OrganizationLogoMetadata,
} from '../../models/company-settings.model';
import { FormField } from '../form-field/form-field';
import { LogoControl } from '../logo-control/logo-control';

/**
 * "Company profile" card of Company setup (handoff §2.5): logo, identity, contact, 4-part
 * company address, default country and time zone. Distinct from the onboarding section.
 */
@Component({
  selector: 'app-company-profile-card',
  imports: [ReactiveFormsModule, InputText, Select, FormField, LogoControl],
  templateUrl: './company-profile-card.html',
  styleUrl: './company-profile-card.scss',
})
export class CompanyProfileCard {
  readonly group = input.required<FormGroup<CompanySetupFormControls>>();
  readonly fieldErrors = input<OrganizationFieldErrors>({});
  readonly submitting = input(false);
  readonly canManage = input.required<boolean>();
  readonly logo = input.required<OrganizationLogoMetadata | null>();
  readonly timezoneOptions = input.required<SelectOption[]>();
  readonly countryOptions = input.required<SelectOption[]>();
  readonly fieldBlur = output<OrganizationFieldKey>();
  readonly unauthorized = output<void>();

  readonly stateOptions = US_STATE_OPTIONS;
  readonly addressFields: readonly { key: OrganizationFieldKey; id: string; label: string }[] = [
    { key: 'organization.addressLine1', id: 'organization-addressLine1', label: 'Street address' },
    { key: 'organization.city', id: 'organization-city', label: 'City' },
    { key: 'organization.stateRegion', id: 'organization-stateRegion', label: 'State' },
    { key: 'organization.postalCode', id: 'organization-postalCode', label: 'ZIP code' },
  ];

  /** The US uses a state list; other countries a free-text region. */
  private readonly country = toSignal(
    toObservable(this.group).pipe(
      switchMap((group) =>
        group.controls.countryCode.valueChanges.pipe(startWith(group.controls.countryCode.value)),
      ),
    ),
    { initialValue: '' },
  );

  usesStateList(): boolean {
    return this.country().trim().toUpperCase() === 'US';
  }

  displayName(): string {
    return this.group().controls.name.value;
  }

  error(field: OrganizationFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  /** `aria-describedby` for a control: its error message when shown. */
  describedBy(field: OrganizationFieldKey, id: string): string | null {
    return this.error(field) !== null ? `${id}-error` : null;
  }
}
