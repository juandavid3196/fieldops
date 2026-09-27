import { Component, computed, input, output } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';

import { FormField } from '../form-field/form-field';
import { FieldErrors, OwnerFormControls } from '../../models/organization-registration.model';

import type { FormGroup } from '@angular/forms';
import type { SimpleFieldKey } from '../../models/organization-registration.model';

/** "Owner account" section (FR-01): the Owner's name, contact and password. */
@Component({
  selector: 'app-owner-account-section',
  imports: [ReactiveFormsModule, InputText, Password, ButtonDirective, FormField],
  templateUrl: './owner-account-section.html',
  styleUrl: './owner-account-section.scss',
})
export class OwnerAccountSection {
  readonly group = input.required<FormGroup<OwnerFormControls>>();
  readonly confirmPasswordControl = input.required<FormControl<string>>();
  readonly fieldErrors = input<FieldErrors>({});
  readonly submitting = input(false);
  readonly fieldBlur = output<SimpleFieldKey>();

  /** PrimeNG lacks a `readonly` input on `p-password`; applied via pass-through to its inner input. */
  readonly passwordReadonlyPt = computed(() => ({
    pcInputText: { root: { readonly: this.submitting() } },
  }));

  error(field: SimpleFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }
}
