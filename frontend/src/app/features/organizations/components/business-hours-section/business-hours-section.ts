import { Component, input, output } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { ExclamationTriangleIcon } from 'primeng/icons/exclamationtriangle';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';

import { WizardCard } from '../wizard-card/wizard-card';
import {
  BusinessHoursControls,
  BusinessHoursDayControls,
  FieldErrors,
  FieldKey,
  WEEKDAYS,
  WEEKDAY_LABELS,
  Weekday,
} from '../../models/organization-registration.model';
import { TIME_OPTIONS } from '../../pages/register-company/register-company.helpers';
import {
  businessHoursEndKey,
  businessHoursStartKey,
  fieldControlId,
} from '../../pages/register-company/register-company.validators';

import type { FormGroup } from '@angular/forms';

/**
 * Wizard "Business hours" card (FR-05): one row per day with an open switch and 30-minute
 * start/end dropdowns, plus Copy Monday to weekdays. The drawer keeps its own editor (AS-02).
 */
@Component({
  selector: 'app-business-hours-section',
  imports: [
    ReactiveFormsModule,
    ButtonDirective,
    Select,
    ToggleSwitch,
    ExclamationTriangleIcon,
    WizardCard,
  ],
  templateUrl: './business-hours-section.html',
  styleUrl: './business-hours-section.scss',
})
export class BusinessHoursSection {
  readonly group = input.required<FormGroup<BusinessHoursControls>>();
  readonly fieldErrors = input<FieldErrors>({});
  readonly submitting = input(false);
  readonly fieldBlur = output<FieldKey>();
  readonly copyMonday = output<void>();

  readonly weekdays = WEEKDAYS;
  readonly timeOptions = TIME_OPTIONS;

  dayLabel(day: Weekday): string {
    return WEEKDAY_LABELS[day];
  }

  dayGroup(day: Weekday): FormGroup<BusinessHoursDayControls> {
    return this.group().controls[day];
  }

  isOpen(day: Weekday): boolean {
    return this.dayGroup(day).controls.open.value;
  }

  rootError(): string | null {
    return this.fieldErrors()['branch.businessHours'] ?? null;
  }

  startError(day: Weekday): string | null {
    return this.fieldErrors()[businessHoursStartKey(day)] ?? null;
  }

  endError(day: Weekday): string | null {
    return this.fieldErrors()[businessHoursEndKey(day)] ?? null;
  }

  controlId(day: Weekday, part: 'open' | 'start' | 'end'): string {
    return part === 'open'
      ? `${fieldControlId(businessHoursStartKey(day))}-toggle`
      : fieldControlId(part === 'start' ? businessHoursStartKey(day) : businessHoursEndKey(day));
  }

  blur(day: Weekday, part: 'start' | 'end'): void {
    this.fieldBlur.emit(part === 'start' ? businessHoursStartKey(day) : businessHoursEndKey(day));
  }
}
