import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';

import { isApiError } from '../../../../core/models/api-error.model';
import { FormField } from '../../../organizations/components/form-field/form-field';
import { TIME_WINDOW_LABELS } from '../../../service-request/service-request.messages';
import {
  GENERIC_ERROR_MESSAGE,
  PortalAppointment,
  RATE_LIMITED_MESSAGE,
  TimeWindow,
} from '../../models/portal.model';
import { PortalAppointmentsService } from '../../services/portal-appointments.service';
import { rescheduleRange, toDateOnly } from '../../utils/portal-format';

export const REASON_LIMIT = 500;
export const RESCHEDULE_DATE_REQUIRED = 'Choose a preferred date.';
export const RESCHEDULE_DATE_RANGE = 'Choose a date from tomorrow up to 90 days from today.';
export const RESCHEDULE_REASON_REQUIRED = 'Tell us why you need to reschedule.';
export const RESCHEDULE_REASON_LONG = 'Reason must be 500 characters or fewer.';
export const RESCHEDULE_ALREADY_REQUESTED = 'You already asked to reschedule this appointment.';
export const RESCHEDULE_FIELD_FALLBACK = 'Check this field and try again.';

export function rescheduleUnavailable(phone: string | null): string {
  return phone === null
    ? "This appointment can't be rescheduled online. Contact the company."
    : `This appointment can't be rescheduled online. Call ${phone}.`;
}

type Field = 'preferredDate' | 'reason';

/** Reschedule dialog (BR-32): collects, validates and submits once; the parent patches its card. */
@Component({
  selector: 'app-portal-reschedule-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, FormField, Select, SpinnerIcon, Textarea],
  templateUrl: './portal-reschedule-dialog.html',
  styles: `
    :host {
      display: contents;
    }

    .dialog {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      min-inline-size: 0;
    }

    .dialog__text,
    .dialog__count {
      margin: 0;
      color: var(--fo-color-text-secondary);
    }

    .dialog__count {
      font-size: 0.8125rem;
      text-align: end;
    }

    .dialog__failure {
      margin: 0;
      color: var(--fo-color-danger-text);
    }
  `,
})
export class PortalRescheduleDialog {
  private readonly appointments = inject(PortalAppointmentsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly dateField = viewChild<ElementRef<HTMLInputElement>>('dateField');
  private trigger: HTMLElement | null = null;

  /** The appointment to reschedule; `null` keeps the dialog closed. */
  readonly appointment = input<PortalAppointment | null>(null);
  /** Organization phone for the "Call <phone>" message. */
  readonly phone = input<string | null>(null);

  readonly dismissed = output<void>();
  readonly requested = output<{ readonly visitId: string; readonly requestedOn: string }>();

  readonly open = computed(() => this.appointment() !== null);
  readonly preferredDate = signal('');
  readonly timeWindow = signal<TimeWindow>('any');
  readonly reason = signal('');
  readonly errors = signal<Partial<Record<Field, string>>>({});
  readonly failure = signal<string | null>(null);
  readonly submitting = signal(false);

  readonly limit = REASON_LIMIT;
  readonly range = computed(() => {
    this.appointment();
    const { min, max } = rescheduleRange();
    return { min: toDateOnly(min), max: toDateOnly(max) };
  });
  readonly windows = (Object.keys(TIME_WINDOW_LABELS) as TimeWindow[]).map((value) => ({
    value,
    label: TIME_WINDOW_LABELS[value],
  }));

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          const active = this.document.activeElement;
          this.trigger = active instanceof HTMLElement ? active : null;
          this.preferredDate.set('');
          this.timeWindow.set('any');
          this.reason.set('');
          this.errors.set({});
          this.failure.set(null);
        });
      }
    });
  }

  close(): void {
    if (this.submitting()) {
      return;
    }
    this.dismissed.emit();
    this.restoreFocus();
  }

  submit(): void {
    const appointment = this.appointment();
    if (appointment === null || this.submitting()) {
      return;
    }
    const errors = this.validate();
    this.errors.set(errors);
    this.failure.set(null);
    if (errors.preferredDate !== undefined || errors.reason !== undefined) {
      this.focusField(
        errors.preferredDate !== undefined ? 'portal-reschedule-date' : 'portal-reschedule-reason',
      );
      return;
    }

    this.submitting.set(true);
    this.appointments
      .requestReschedule(appointment.visitId, {
        preferredDate: this.preferredDate(),
        timeWindow: this.timeWindow(),
        reason: this.reason().trim(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.requested.emit({ visitId: appointment.visitId, requestedOn: result.requestedOn });
          this.restoreFocus();
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  private validate(): Partial<Record<Field, string>> {
    const errors: Partial<Record<Field, string>> = {};
    const { min, max } = this.range();
    const date = this.preferredDate();
    if (date === '') {
      errors.preferredDate = RESCHEDULE_DATE_REQUIRED;
    } else if (date < min || date > max) {
      errors.preferredDate = RESCHEDULE_DATE_RANGE;
    }
    const length = this.reason().trim().length;
    if (length === 0) {
      errors.reason = RESCHEDULE_REASON_REQUIRED;
    } else if (length > REASON_LIMIT) {
      errors.reason = RESCHEDULE_REASON_LONG;
    }
    return errors;
  }

  private fail(error: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.status === 409 && apiError.code === 'reschedule_already_requested') {
      this.failure.set(RESCHEDULE_ALREADY_REQUESTED);
    } else if (apiError?.status === 409) {
      this.failure.set(rescheduleUnavailable(this.phone()));
    } else if (apiError?.kind === 'validation') {
      const fields = apiError.fieldErrors;
      const mapped: Partial<Record<Field, string>> = {};
      if (fields['preferredDate'] !== undefined) {
        mapped.preferredDate = RESCHEDULE_DATE_RANGE;
      }
      if (fields['reason'] !== undefined) {
        mapped.reason = RESCHEDULE_REASON_REQUIRED;
      }
      if (Object.keys(mapped).length > 0) {
        this.errors.set(mapped);
      } else {
        this.failure.set(RESCHEDULE_FIELD_FALLBACK);
      }
    } else if (apiError?.kind === 'rate-limited') {
      this.failure.set(RATE_LIMITED_MESSAGE);
    } else {
      this.failure.set(GENERIC_ERROR_MESSAGE);
    }
  }

  private focusField(id: string): void {
    afterNextRender(() => this.document.getElementById(id)?.focus(), { injector: this.injector });
  }

  private restoreFocus(): void {
    const trigger = this.trigger;
    this.trigger = null;
    afterNextRender(
      () => (trigger !== null && this.host.nativeElement.isConnected ? trigger.focus() : undefined),
      { injector: this.injector },
    );
  }
}
