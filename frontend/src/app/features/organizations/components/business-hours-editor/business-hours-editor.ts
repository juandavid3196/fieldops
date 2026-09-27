import { Component, input, output } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { ExclamationTriangleIcon } from 'primeng/icons/exclamationtriangle';
import { ToggleSwitch } from 'primeng/toggleswitch';

import {
  BusinessHoursControls,
  FieldErrors,
  Weekday,
  WEEKDAYS,
  WEEKDAY_LABELS,
} from '../../models/organization-registration.model';
import {
  businessHoursEndKey,
  businessHoursStartKey,
  fieldControlId,
} from '../../pages/register-company/register-company.validators';

import type { FormGroup } from '@angular/forms';
import type {
  BusinessHoursDayControls,
  FieldKey,
} from '../../models/organization-registration.model';

/**
 * 7-day business hours editor (FR-15): open toggle plus start/end time per
 * day. Always full width. The whole-object `branch.businessHours` error is
 * server-only and renders once above the days.
 */
@Component({
  selector: 'app-business-hours-editor',
  imports: [ReactiveFormsModule, ToggleSwitch, ExclamationTriangleIcon],
  templateUrl: './business-hours-editor.html',
  styleUrl: './business-hours-editor.scss',
})
export class BusinessHoursEditor {
  readonly group = input.required<FormGroup<BusinessHoursControls>>();
  readonly fieldErrors = input<FieldErrors>({});
  readonly rootError = input<string | null>(null);
  readonly submitting = input(false);
  readonly fieldBlur = output<FieldKey>();

  readonly weekdays = WEEKDAYS;

  dayLabel(day: Weekday): string {
    return WEEKDAY_LABELS[day];
  }

  dayGroup(day: Weekday): FormGroup<BusinessHoursDayControls> {
    return this.group().controls[day];
  }

  isOpen(day: Weekday): boolean {
    return this.dayGroup(day).controls.open.value;
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

  onBlur(day: Weekday, part: 'start' | 'end'): void {
    this.fieldBlur.emit(part === 'start' ? businessHoursStartKey(day) : businessHoursEndKey(day));
  }
}
