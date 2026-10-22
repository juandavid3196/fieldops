import { Component, ElementRef, input, output, viewChild } from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  CompanySetupFormControls,
  OrganizationFieldErrors,
  OrganizationFieldKey,
} from '../../models/company-settings.model';
import { FormField } from '../form-field/form-field';

import { t } from '../../../../core/i18n/translate';
export const NUMBERING_HELPER = (): string =>
  t('organizations.documentNumberingCard.numberingHelper');

/** "Document numbering" card of Company setup (handoff §2.8). */
@Component({
  selector: 'app-document-numbering-card',
  imports: [TranslocoPipe, ReactiveFormsModule, InputText, InputNumber, FormField],
  templateUrl: './document-numbering-card.html',
  styleUrl: './document-numbering-card.scss',
})
export class DocumentNumberingCard {
  private readonly sequencesLink =
    viewChild.required<ElementRef<HTMLButtonElement>>('sequencesLink');

  readonly group = input.required<FormGroup<CompanySetupFormControls>>();
  readonly fieldErrors = input<OrganizationFieldErrors>({});
  readonly submitting = input(false);
  readonly canManage = input.required<boolean>();
  readonly fieldBlur = output<OrganizationFieldKey>();
  readonly sequencesRequested = output<void>();

  get helper(): string {
    return NUMBERING_HELPER();
  }

  error(field: OrganizationFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  /** Moves focus to **Edit sequences** (server errors for the quote/work order numbers). */
  focusSequencesLink(): void {
    this.sequencesLink().nativeElement.focus();
  }
}
