import { Component, input, output } from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { TranslocoPipe } from '@jsverse/transloco';

import { comingSoonPath } from '../../../../core/config/coming-soon-modules';
import { SelectOption } from '../../data/display-names';
import {
  CompanySetupFormControls,
  OrganizationFieldErrors,
  OrganizationFieldKey,
} from '../../models/company-settings.model';
import { FormField } from '../form-field/form-field';

import { t } from '../../../../core/i18n/translate';
export const CURRENCY_WARNING = (): string => t('organizations.taxesCurrencyCard.currencyWarning');

/** "Taxes & currency" card of Company setup (handoff §2.8). */
@Component({
  selector: 'app-taxes-currency-card',
  imports: [
    TranslocoPipe,
    ReactiveFormsModule,
    RouterLink,
    InputNumber,
    Select,
    ToggleSwitch,
    FormField,
  ],
  templateUrl: './taxes-currency-card.html',
  styleUrl: './taxes-currency-card.scss',
})
export class TaxesCurrencyCard {
  readonly group = input.required<FormGroup<CompanySetupFormControls>>();
  readonly fieldErrors = input<OrganizationFieldErrors>({});
  readonly submitting = input(false);
  readonly currencyOptions = input.required<SelectOption[]>();
  readonly fieldBlur = output<OrganizationFieldKey>();

  get warning(): string {
    return CURRENCY_WARNING();
  }
  readonly taxRatesLink = comingSoonPath('tax-rates');

  error(field: OrganizationFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }
}
