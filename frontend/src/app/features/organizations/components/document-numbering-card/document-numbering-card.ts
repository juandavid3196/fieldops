import { Component, ElementRef, input, output, viewChild } from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';

import {
  CompanySetupFormControls,
  OrganizationFieldErrors,
  OrganizationFieldKey,
} from '../../models/company-settings.model';
import { FormField } from '../form-field/form-field';

export const NUMBERING_HELPER =
  'Prefixes appear before the sequential number, for example INV-1049.';

/** "Document numbering" card of Company setup (handoff §2.8). */
@Component({
  selector: 'app-document-numbering-card',
  imports: [ReactiveFormsModule, InputText, InputNumber, FormField],
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

  readonly helper = NUMBERING_HELPER;

  error(field: OrganizationFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  /** Moves focus to **Edit sequences** (server errors for the quote/work order numbers). */
  focusSequencesLink(): void {
    this.sequencesLink().nativeElement.focus();
  }
}
