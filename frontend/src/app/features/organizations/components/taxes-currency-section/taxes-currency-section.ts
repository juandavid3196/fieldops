import { Component, input, output } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';

import { FormField } from '../form-field/form-field';
import { WizardCard } from '../wizard-card/wizard-card';
import { WizardNote } from '../wizard-note/wizard-note';
import { SelectOption } from '../../data/display-names';
import {
  FieldErrors,
  OrganizationFormControls,
} from '../../models/organization-registration.model';

import type { FormGroup } from '@angular/forms';
import type { SimpleFieldKey } from '../../models/organization-registration.model';

/** "Taxes & currency" section (FR-01): currency and default tax rate. */
@Component({
  selector: 'app-taxes-currency-section',
  imports: [ReactiveFormsModule, InputNumber, Select, FormField, WizardCard, WizardNote],
  templateUrl: './taxes-currency-section.html',
  styleUrl: './taxes-currency-section.scss',
})
export class TaxesCurrencySection {
  readonly group = input.required<FormGroup<OrganizationFormControls>>();
  readonly fieldErrors = input<FieldErrors>({});
  readonly submitting = input(false);
  readonly currencyOptions = input.required<SelectOption[]>();
  readonly fieldBlur = output<SimpleFieldKey>();

  error(field: SimpleFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }
}
