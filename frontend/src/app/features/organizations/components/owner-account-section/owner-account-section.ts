import { Component, input, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Checkbox } from 'primeng/checkbox';
import { InputText } from 'primeng/inputtext';

import { FormField } from '../form-field/form-field';
import { WizardCard } from '../wizard-card/wizard-card';
import { WizardNote } from '../wizard-note/wizard-note';
import { FieldErrors, OwnerFormControls } from '../../models/organization-registration.model';
import { PasswordRequirements } from '../../pages/register-company/register-company.helpers';

import type { FormGroup } from '@angular/forms';
import type { SimpleFieldKey } from '../../models/organization-registration.model';

/** "Your account" card (wizard step 3): the Owner's name, contact and password. */
@Component({
  selector: 'app-owner-account-section',
  imports: [ReactiveFormsModule, InputText, Checkbox, FormField, WizardCard, WizardNote],
  templateUrl: './owner-account-section.html',
  styleUrl: './owner-account-section.scss',
})
export class OwnerAccountSection {
  readonly group = input.required<FormGroup<OwnerFormControls>>();
  readonly confirmPasswordControl = input.required<FormControl<string>>();
  readonly fieldErrors = input<FieldErrors>({});
  readonly submitting = input(false);
  /** BR-07 states, computed by the page from the current password and owner email. */
  readonly requirements = input.required<PasswordRequirements>();
  readonly fieldBlur = output<SimpleFieldKey>();

  /** Not a wizard value: never part of the form, the request or BR-08. */
  readonly showPassword = new FormControl(false, { nonNullable: true });
  readonly passwordsVisible = toSignal(this.showPassword.valueChanges, { initialValue: false });

  error(field: SimpleFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }
}
