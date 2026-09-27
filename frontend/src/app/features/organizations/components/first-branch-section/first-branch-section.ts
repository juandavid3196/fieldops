import { Component, input, output } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';

import { BusinessHoursEditor } from '../business-hours-editor/business-hours-editor';
import { FormField } from '../form-field/form-field';
import { SelectOption } from '../../data/display-names';
import {
  BranchFormControls,
  FieldErrors,
  FieldKey,
} from '../../models/organization-registration.model';

import type { FormGroup } from '@angular/forms';
import type { SimpleFieldKey } from '../../models/organization-registration.model';

/** "First branch" section (FR-01): branch fields, address and business hours. */
@Component({
  selector: 'app-first-branch-section',
  imports: [ReactiveFormsModule, InputText, Select, FormField, BusinessHoursEditor],
  templateUrl: './first-branch-section.html',
  styleUrl: './first-branch-section.scss',
})
export class FirstBranchSection {
  readonly group = input.required<FormGroup<BranchFormControls>>();
  readonly fieldErrors = input<FieldErrors>({});
  readonly submitting = input(false);
  readonly timezoneOptions = input.required<SelectOption[]>();
  readonly countryOptions = input.required<SelectOption[]>();
  readonly fieldBlur = output<FieldKey>();
  readonly branchTimezoneChanged = output<void>();

  error(field: SimpleFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }
}
