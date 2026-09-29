import { Component, computed, input, output } from '@angular/core';

import { SelectOption } from '../../data/display-names';
import { RegisterCompanyFormValue } from '../../models/organization-registration.model';
import {
  FormStep,
  cityLine,
  orNotProvided,
  ownerInitials,
  summarizeBusinessHours,
} from '../../pages/register-company/register-company.helpers';
import { WizardCard } from '../wizard-card/wizard-card';

export interface ReviewRow {
  readonly label: string;
  readonly lines: readonly string[];
}

/** Review step (FR-08): the "Everything is ready" banner and four summary cards (BR-09). */
@Component({
  selector: 'app-review-step',
  imports: [WizardCard],
  templateUrl: './review-step.html',
  styleUrl: './review-step.scss',
})
export class ReviewStep {
  readonly value = input.required<RegisterCompanyFormValue>();
  readonly timezoneOptions = input.required<readonly SelectOption[]>();
  readonly currencyOptions = input.required<readonly SelectOption[]>();
  readonly countryOptions = input.required<readonly SelectOption[]>();
  readonly submitting = input(false);
  readonly edit = output<FormStep>();

  readonly companyRows = computed<readonly ReviewRow[]>(() => {
    const { organization } = this.value();
    return [
      { label: 'Display name', lines: [organization.name.trim()] },
      { label: 'Legal business name', lines: [organization.legalName.trim()] },
      { label: 'Business email', lines: [organization.email.trim()] },
      { label: 'Business phone', lines: [organization.phone.trim()] },
      { label: 'Tax ID', lines: [orNotProvided(organization.taxId)] },
      {
        label: 'Company time zone',
        lines: [this.labelOf(this.timezoneOptions(), organization.timezone)],
      },
    ];
  });

  readonly branchRows = computed<readonly ReviewRow[]>(() => {
    const { branch } = this.value();
    return [
      { label: 'Branch name', lines: [branch.name.trim()] },
      { label: 'Branch code', lines: [branch.code.trim().toUpperCase()] },
      {
        label: 'Address',
        lines: [
          branch.addressLine1.trim(),
          cityLine(branch.city, branch.stateRegion, branch.postalCode),
          this.labelOf(this.countryOptions(), branch.countryCode),
        ],
      },
      { label: 'Branch phone', lines: [orNotProvided(branch.phone)] },
      { label: 'Branch email', lines: [orNotProvided(branch.email)] },
      {
        label: 'Branch time zone',
        lines: [this.labelOf(this.timezoneOptions(), branch.timezone)],
      },
      {
        label: 'Business hours',
        lines: [summarizeBusinessHours(branch.businessHours, branch.timezone)],
      },
    ];
  });

  readonly settingsRows = computed<readonly ReviewRow[]>(() => {
    const { organization } = this.value();
    return [
      {
        label: 'Default currency',
        lines: [this.labelOf(this.currencyOptions(), organization.currency)],
      },
      { label: 'Default tax rate', lines: [`${organization.defaultTaxRate}%`] },
      { label: 'Quote prefix', lines: [organization.quotePrefix.trim().toUpperCase()] },
      { label: 'Work order prefix', lines: [organization.workOrderPrefix.trim().toUpperCase()] },
      { label: 'Invoice prefix', lines: [organization.invoicePrefix.trim().toUpperCase()] },
      { label: 'Next invoice number', lines: [String(organization.nextInvoiceNumber)] },
    ];
  });

  readonly owner = computed(() => {
    const { owner } = this.value();
    return {
      name: `${owner.firstName.trim()} ${owner.lastName.trim()}`,
      email: owner.email.trim(),
      phone: orNotProvided(owner.phone),
      initials: ownerInitials(owner.firstName, owner.lastName),
    };
  });

  private labelOf(options: readonly SelectOption[], code: string): string {
    return options.find((option) => option.code === code)?.label ?? code;
  }
}
