import { Component, computed, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

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

import { t } from '../../../../core/i18n/translate';
export interface ReviewRow {
  readonly label: string;
  readonly lines: readonly string[];
}

/** Review step (FR-08): the "Everything is ready" banner and four summary cards (BR-09). */
@Component({
  selector: 'app-review-step',
  imports: [TranslocoPipe, WizardCard],
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
      { label: t('organizations.reviewStep.displayName'), lines: [organization.name.trim()] },
      {
        label: t('organizations.reviewStep.legalBusinessName'),
        lines: [organization.legalName.trim()],
      },
      { label: t('organizations.reviewStep.businessEmail'), lines: [organization.email.trim()] },
      { label: t('organizations.reviewStep.businessPhone'), lines: [organization.phone.trim()] },
      { label: t('organizations.reviewStep.taxId'), lines: [orNotProvided(organization.taxId)] },
      {
        label: t('organizations.reviewStep.companyTimeZone'),
        lines: [this.labelOf(this.timezoneOptions(), organization.timezone)],
      },
    ];
  });

  readonly branchRows = computed<readonly ReviewRow[]>(() => {
    const { branch } = this.value();
    return [
      { label: t('organizations.reviewStep.branchName'), lines: [branch.name.trim()] },
      {
        label: t('organizations.reviewStep.branchCode'),
        lines: [branch.code.trim().toUpperCase()],
      },
      {
        label: t('common.address'),
        lines: [
          branch.addressLine1.trim(),
          cityLine(branch.city, branch.stateRegion, branch.postalCode),
          this.labelOf(this.countryOptions(), branch.countryCode),
        ],
      },
      { label: t('organizations.reviewStep.branchPhone'), lines: [orNotProvided(branch.phone)] },
      { label: t('organizations.reviewStep.branchEmail'), lines: [orNotProvided(branch.email)] },
      {
        label: t('organizations.reviewStep.branchTimeZone'),
        lines: [this.labelOf(this.timezoneOptions(), branch.timezone)],
      },
      {
        label: t('organizations.reviewStep.businessHours'),
        lines: [summarizeBusinessHours(branch.businessHours, branch.timezone)],
      },
    ];
  });

  readonly settingsRows = computed<readonly ReviewRow[]>(() => {
    const { organization } = this.value();
    return [
      {
        label: t('organizations.reviewStep.defaultCurrency'),
        lines: [this.labelOf(this.currencyOptions(), organization.currency)],
      },
      {
        label: t('organizations.reviewStep.defaultTaxRate'),
        lines: [`${organization.defaultTaxRate}%`],
      },
      {
        label: t('organizations.reviewStep.quotePrefix'),
        lines: [organization.quotePrefix.trim().toUpperCase()],
      },
      {
        label: t('organizations.reviewStep.workOrderPrefix'),
        lines: [organization.workOrderPrefix.trim().toUpperCase()],
      },
      {
        label: t('organizations.reviewStep.invoicePrefix'),
        lines: [organization.invoicePrefix.trim().toUpperCase()],
      },
      {
        label: t('organizations.reviewStep.nextInvoiceNumber'),
        lines: [String(organization.nextInvoiceNumber)],
      },
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
