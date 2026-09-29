import { Component, input, output } from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';

import { comingSoonPath } from '../../../../core/config/coming-soon-modules';
import { SelectOption } from '../../data/display-names';
import {
  CompanySetupFormControls,
  OrganizationFieldErrors,
  OrganizationFieldKey,
} from '../../models/company-settings.model';
import { FormField } from '../form-field/form-field';

export const CURRENCY_WARNING =
  'Changing the currency after invoices exist requires confirmation and may affect historical records.';

/** "Taxes & currency" card of Company setup (handoff §2.8). */
@Component({
  selector: 'app-taxes-currency-card',
  imports: [ReactiveFormsModule, RouterLink, InputNumber, Select, ToggleSwitch, FormField],
  templateUrl: './taxes-currency-card.html',
  styleUrl: './taxes-currency-card.scss',
})
export class TaxesCurrencyCard {
  readonly group = input.required<FormGroup<CompanySetupFormControls>>();
  readonly fieldErrors = input<OrganizationFieldErrors>({});
  readonly submitting = input(false);
  readonly currencyOptions = input.required<SelectOption[]>();
  readonly fieldBlur = output<OrganizationFieldKey>();

  readonly warning = CURRENCY_WARNING;
  readonly taxRatesLink = comingSoonPath('tax-rates');

  error(field: OrganizationFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }
}
