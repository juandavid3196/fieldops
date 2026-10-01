import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import {
  ExceptionKind,
  ExceptionRequest,
  TechnicianException,
} from '../../models/skills-availability.model';
import { SkillsAvailabilityService } from '../../services/skills-availability.service';
import { KIND_LABELS, TIME_OPTIONS, toMinutes } from '../../utils/skills-availability';

export const EXCEPTION_CONFLICT_MESSAGES: Readonly<Record<string, string>> = {
  exception_date_taken: 'This technician already has an active exception on this date.',
  exception_past: "Past exceptions can't be changed.",
  exception_cancelled: 'Activate this exception before editing it.',
  exception_stale: 'This exception was changed by someone else. Reload to see the latest version.',
};
export const DUPLICATE_DATE_MESSAGE = EXCEPTION_CONFLICT_MESSAGES['exception_date_taken'];

/** BR-14/BR-15 conflict text: by backend `code` (never ProblemDetails text), else the generic message. */
export function exceptionConflictMessage(error: ApiError, creating: boolean): string {
  return (
    (error.code ? EXCEPTION_CONFLICT_MESSAGES[error.code] : undefined) ??
    (creating ? DUPLICATE_DATE_MESSAGE : error.message)
  );
}

const KIND_OPTIONS = (Object.keys(KIND_LABELS) as ExceptionKind[]).map((value) => ({
  value,
  label: value === 'unavailable' ? 'Unavailable all day' : KIND_LABELS[value],
}));

interface FormValue {
  readonly date: string;
  readonly kind: ExceptionKind | null;
  readonly start: string | null;
  readonly end: string | null;
  readonly reason: string;
}

type Errors = Partial<Record<'date' | 'kind' | 'start' | 'end' | 'reason', string>>;

const EMPTY: FormValue = { date: '', kind: null, start: null, end: null, reason: '' };

/** Pure client validation per the Exception validation table. */
export function validateException(value: FormValue, today: string): Errors {
  const errors: Errors = {};
  if (value.date === '' || value.date < today) {
    errors.date = 'Choose today or a future date.';
  }
  if (value.kind === null) {
    errors.kind = 'Choose an availability type.';
  } else if (value.kind !== 'unavailable') {
    if (value.start === null || value.end === null) {
      errors.start = 'Choose a start and end time.';
    } else if (toMinutes(value.start) >= toMinutes(value.end)) {
      errors.end = 'End time must be after start time.';
    }
  }
  const reason = value.reason.trim();
  if (reason === '') {
    errors.reason = 'Enter a reason.';
  } else if (reason.length > 200) {
    errors.reason = 'Use 200 characters or fewer.';
  }
  return errors;
}

/** Add/Edit exception dialog (BR-12–BR-15): validation, inline 409, discard confirmation when dirty. */
@Component({
  selector: 'app-exception-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, InputText, Message, Select, SpinnerIcon],
  templateUrl: './exception-dialog.html',
  styleUrl: './exception-dialog.scss',
})
export class ExceptionDialog {
  private readonly service = inject(SkillsAvailabilityService);
  private readonly messages = inject(MessageService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly visible = model(false);
  readonly technicianId = input.required<string>();
  /** Null adds an exception. */
  readonly exception = input<TechnicianException | null>(null);
  readonly today = input.required<string>();

  readonly saved = output<void>();
  readonly unauthorized = output<void>();
  readonly unavailable = output<void>();

  protected readonly kindOptions = KIND_OPTIONS;
  protected readonly timeOptions = TIME_OPTIONS;
  protected readonly form = signal<FormValue>(EMPTY);
  private readonly initial = signal<FormValue>(EMPTY);
  protected readonly errors = signal<Errors>({});
  protected readonly conflict = signal<string | null>(null);
  protected readonly failed = signal(false);
  protected readonly submitting = signal(false);
  protected readonly editing = computed(() => this.exception() !== null);
  protected readonly timed = computed(() => this.form().kind !== 'unavailable');
  readonly dirty = computed(() => JSON.stringify(this.form()) !== JSON.stringify(this.initial()));

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.reset());
      }
    });
  }

  private reset(): void {
    const exception = this.exception();
    const value: FormValue = exception
      ? {
          date: exception.date,
          kind: exception.kind,
          start: exception.start?.slice(0, 5) ?? null,
          end: exception.end?.slice(0, 5) ?? null,
          reason: exception.reason,
        }
      : EMPTY;
    this.form.set(value);
    this.initial.set(value);
    this.errors.set({});
    this.conflict.set(null);
    this.failed.set(false);
    this.submitting.set(false);
  }

  protected patch(change: Partial<FormValue>): void {
    this.form.update((value) => ({ ...value, ...change }));
    this.conflict.set(null);
  }

  /** Close, asking first when the form is dirty. */
  close(): void {
    if (this.submitting()) {
      return;
    }
    if (!this.dirty()) {
      this.visible.set(false);
      return;
    }
    this.confirmation.confirm(
      discardChangesConfirmation({
        subject: 'this exception',
        accept: () => this.visible.set(false),
      }),
    );
  }

  protected onVisibleChange(open: boolean): void {
    if (open) {
      this.visible.set(true);
    } else {
      this.close();
    }
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    const value = this.form();
    const errors = validateException(value, this.today());
    this.errors.set(errors);
    if (Object.keys(errors).length > 0 || value.kind === null) {
      return;
    }
    const body: ExceptionRequest = {
      date: value.date,
      kind: value.kind,
      reason: value.reason.trim(),
      ...(value.kind !== 'unavailable' && value.start !== null && value.end !== null
        ? { start: value.start, end: value.end }
        : {}),
    };
    const exception = this.exception();
    const request = exception
      ? this.service.updateException(this.technicianId(), exception.id, {
          ...body,
          version: exception.version,
        })
      : this.service.createException(this.technicianId(), body);
    this.submitting.set(true);
    this.conflict.set(null);
    this.failed.set(false);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.submitting.set(false);
        this.visible.set(false);
        this.messages.add({
          severity: 'success',
          summary: exception ? 'Exception updated.' : 'Exception added.',
        });
        this.saved.emit();
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.handleError(error, exception === null);
      },
    });
  }

  private handleError(error: unknown, creating: boolean): void {
    if (!isApiError(error)) {
      this.failed.set(true);
      return;
    }
    switch (error.kind) {
      case 'unauthorized':
        this.unauthorized.emit();
        return;
      case 'not-found':
        this.visible.set(false);
        this.unavailable.emit();
        return;
      case 'conflict':
        this.conflict.set(exceptionConflictMessage(error, creating));
        return;
      case 'validation': {
        const first = (key: string): string | undefined => error.fieldErrors[key]?.[0];
        this.errors.set({
          date: first('date'),
          kind: first('kind'),
          start: first('start'),
          end: first('end'),
          reason: first('reason'),
        });
        return;
      }
      default:
        this.failed.set(true);
    }
  }
}
