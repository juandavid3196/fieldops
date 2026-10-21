import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { TranslocoPipe } from '@jsverse/transloco';

import { FormField } from '../../../organizations/components/form-field/form-field';
import { NO_EMAIL_MESSAGE } from '../../models/requests.model';
import {
  AssessmentFormValue,
  AssessmentMode,
  DURATION_OPTIONS,
  Option,
  WINDOW_OPTIONS,
} from '../../utils/assessment-schedule';

import { t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
export const PREVIEW_PLACEHOLDER = (): string => t('requests.assessmentForm.previewPlaceholder');
export const SMS_UNAVAILABLE_MESSAGE = (): string =>
  t('requests.assessmentForm.smsUnavailableMessage');

/**
 * Assessment visit fields (BR-08 to BR-14, BR-18, BR-20): branch (branchless requests only), date,
 * arrival window, duration, technician, purpose, internal instructions, message preview, notify
 * toggles and buttons. Presentational: the page owns the value, validation and requests.
 */
@Component({
  selector: 'app-assessment-form',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    FormsModule,
    ButtonDirective,
    InputText,
    Select,
    SpinnerIcon,
    Textarea,
    ToggleSwitch,
    FormField,
  ],
  templateUrl: './assessment-form.html',
  styleUrl: './assessment-form.scss',
})
export class AssessmentForm {
  readonly value = input.required<AssessmentFormValue>();
  readonly mode = input.required<AssessmentMode>();
  readonly needsBranch = input(false);
  readonly branches = input<Option<string>[]>([]);
  readonly technicians = input<Option<string>[]>([]);
  /** Local `YYYY-MM-DD` of today in the organization time zone (earliest date). */
  readonly minDate = input('');
  /** Field errors keyed by field (`date`, `window`, `duration`, `technicianId`, ...). */
  readonly errors = input<Readonly<Record<string, string>>>({});
  readonly preview = input<string | null>(null);
  readonly emailAvailable = input(true);
  /** Submit or cancel is in flight. */
  readonly submitting = input(false);

  readonly valueChange = output<AssessmentFormValue>();
  readonly submitted = output<void>();
  readonly cancelled = output<void>();
  readonly cancelAssessment = output<void>();

  readonly windowOptions = WINDOW_OPTIONS;
  readonly durationOptions = DURATION_OPTIONS;
  get previewPlaceholder(): string {
    return PREVIEW_PLACEHOLDER();
  }
  get noEmailMessage(): string {
    return NO_EMAIL_MESSAGE();
  }
  get smsMessage(): string {
    return SMS_UNAVAILABLE_MESSAGE();
  }

  readonly primaryLabel = computed(() =>
    this.mode() === 'reschedule'
      ? t('requests.assessmentForm.saveChanges')
      : t('requests.assessmentForm.scheduleAssessment'),
  );

  /** The disabled Email toggle exposes its helper text through its accessible name. */
  readonly emailLabelledBy = computed(() =>
    this.emailAvailable()
      ? 'assessment-notify-email-label'
      : 'assessment-notify-email-label assessment-notify-email-helper',
  );

  patch(change: Partial<AssessmentFormValue>): void {
    this.valueChange.emit({ ...this.value(), ...change });
  }

  error(field: string): string | null {
    return this.errors()[field] ?? null;
  }

  describedBy(field: string): string | null {
    return this.error(field) === null ? null : `assessment-${field}-error`;
  }
}
