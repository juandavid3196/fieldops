import { Component, input, output } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';

import { FormField } from '../form-field/form-field';
import { WizardCard } from '../wizard-card/wizard-card';
import {
  FieldErrors,
  OrganizationFormControls,
} from '../../models/organization-registration.model';

import type { FormGroup } from '@angular/forms';
import type { SimpleFieldKey } from '../../models/organization-registration.model';

/** "Document numbering" section (FR-01): prefixes and next invoice number. */
@Component({
  selector: 'app-document-numbering-section',
  imports: [ReactiveFormsModule, InputText, InputNumber, FormField, WizardCard],
  templateUrl: './document-numbering-section.html',
  styleUrl: './document-numbering-section.scss',
})
export class DocumentNumberingSection {
  readonly group = input.required<FormGroup<OrganizationFormControls>>();
  readonly fieldErrors = input<FieldErrors>({});
  readonly submitting = input(false);
  /** BR-06 live examples line, built by the page from the current values. */
  readonly examples = input.required<string>();
  readonly fieldBlur = output<SimpleFieldKey>();

  error(field: SimpleFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }
}
