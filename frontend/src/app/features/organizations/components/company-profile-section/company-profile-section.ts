import { Component, input, output } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';

import { FormField } from '../form-field/form-field';
import { SelectOption } from '../../data/display-names';
import {
  FieldErrors,
  OrganizationFormControls,
} from '../../models/organization-registration.model';

import type { FormGroup } from '@angular/forms';
import type { SimpleFieldKey } from '../../models/organization-registration.model';

/** "Company profile" section (FR-01): name, legal name, tax ID, email, phone, time zone. */
@Component({
  selector: 'app-company-profile-section',
  imports: [ReactiveFormsModule, InputText, Select, FormField],
  templateUrl: './company-profile-section.html',
  styleUrl: './company-profile-section.scss',
})
export class CompanyProfileSection {
  readonly group = input.required<FormGroup<OrganizationFormControls>>();
  readonly fieldErrors = input<FieldErrors>({});
  readonly submitting = input(false);
  readonly timezoneOptions = input.required<SelectOption[]>();
  readonly fieldBlur = output<SimpleFieldKey>();

  error(field: SimpleFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }
}
