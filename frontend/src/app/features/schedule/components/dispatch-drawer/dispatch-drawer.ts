import { DOCUMENT } from '@angular/common';
import {
  Component,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { Textarea } from 'primeng/textarea';
import { Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { visitStatusLabel } from '../../../jobs/utils/work-order-format';
import { FormField } from '../../../organizations/components/form-field/form-field';
import {
  ArrivalWindowCode,
  Check,
  DispatchBody,
  DispatchTechnician,
  VisitDispatchDetail,
  VisitDispatchResult,
  VisitEvaluation,
} from '../../models/schedule.model';
import { ScheduleService } from '../../services/schedule.service';
import { ProposedBlock } from '../../services/schedule-page.store';
import {
  MAX_NOTE,
  MAX_REASON,
  MAX_TECHNICIANS,
  MESSAGES,
  MIN_REASON,
  PRIORITY_ICONS,
  PRIORITY_TEXT,
  arrivalChoices,
  impactText,
  nextText,
  previousText,
  timeChoices,
} from '../../utils/dispatch-form';
import { clockMinutes } from '../../utils/schedule-range';

export const EVALUATION_DEBOUNCE_MS = 300;
export const LOCKED_MESSAGE = "This visit has started or is closed and can't be changed.";
export const CHANGED_MESSAGE =
  'This visit was changed by someone else. Reload to see the latest version.';
export const SAVE_FAILED_MESSAGE = "We couldn't save this visit. Try again.";
export const LOAD_FAILED_MESSAGE = "We couldn't load this visit.";
export const EVALUATION_FAILED_MESSAGE = "We couldn't check availability.";
export const NO_TECHNICIANS_MESSAGE = 'No active technicians in this branch.';
export const NEEDS_SCHEDULE_MESSAGE = 'Set a date and time before assigning technicians.';
export const SMS_UNAVAILABLE_MESSAGE = "SMS isn't available yet.";
export const NO_EMAIL_MESSAGE = 'This customer has no email address.';

type FieldKey =
  | 'date'
  | 'start'
  | 'end'
  | 'arrivalWindow'
  | 'technicianIds'
  | 'primaryTechnicianId'
  | 'dispatchNote'
  | 'overrideReason'
  | 'notifyCustomer';

const FOCUS_ORDER: readonly FieldKey[] = [
  'date',
  'start',
  'end',
  'arrivalWindow',
  'technicianIds',
  'primaryTechnicianId',
  'dispatchNote',
  'overrideReason',
  'notifyCustomer',
];
const CONTROL_IDS: Readonly<Record<FieldKey, string>> = {
  date: 'dispatch-date',
  start: 'dispatch-start',
  end: 'dispatch-end',
  arrivalWindow: 'dispatch-arrival',
  technicianIds: 'dispatch-technicians',
  primaryTechnicianId: 'dispatch-primary',
  dispatchNote: 'dispatch-note',
  overrideReason: 'dispatch-reason',
  notifyCustomer: 'dispatch-notify',
};

interface FormState {
  readonly date: string;
  readonly start: string | null;
  readonly end: string | null;
  readonly arrivalWindow: ArrivalWindowCode;
  readonly technicianIds: readonly string[];
  readonly primaryId: string | null;
  readonly note: string;
  readonly notify: boolean;
  readonly sendDetails: boolean;
  readonly reason: string;
}

interface EvaluationInput {
  readonly date: string;
  readonly start: string;
  readonly end: string;
  readonly technicianIds: readonly string[];
  readonly primaryTechnicianId: string | null;
}

type LoadState = 'loading' | 'ready' | 'error';
type SaveProblem = 'changed' | 'locked' | 'failed' | null;
type LoadResult =
  | { readonly ok: true; readonly detail: VisitDispatchDetail }
  | { readonly ok: false; readonly notFound: boolean };
type EvaluationResult =
  | { readonly ok: true; readonly evaluation: VisitEvaluation }
  | {
      readonly ok: false;
      readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
      readonly locked: boolean;
    };

export interface DispatchSaved {
  readonly result: VisitDispatchResult;
  /** Toast text, e.g. "Visit assigned. Customer notified by email.". */
  readonly message: string;
}

function formFrom(detail: VisitDispatchDetail): FormState {
  const values = detail.values;
  return {
    date: values.date ?? '',
    start: values.start,
    end: values.end,
    arrivalWindow: values.arrivalWindow,
    technicianIds: values.technicianIds,
    primaryId: values.primaryTechnicianId,
    note: values.dispatchNote ?? '',
    notify: values.notifyCustomer && detail.customer.hasEmail,
    sendDetails: values.sendTechnicianDetails,
    reason: '',
  };
}

/**
 * "Assign work order" drawer (BR-08, BR-09, BR-20). Owns its form, the debounced evaluation
 * stream, the single save request and its dirty state; the page owns closing and refreshing.
 */
@Component({
  selector: 'app-dispatch-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    Checkbox,
    DrawerShell,
    FormField,
    InputText,
    Message,
    MultiSelect,
    Select,
    Skeleton,
    Tag,
    Textarea,
  ],
  templateUrl: './dispatch-drawer.html',
  styleUrl: './dispatch-drawer.scss',
})
export class DispatchDrawer {
  private readonly api = inject(ScheduleService);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  readonly visitId = input<string | null>(null);

  readonly closeRequested = output<void>();
  readonly saved = output<DispatchSaved>();
  readonly unavailable = output<void>();
  readonly dirtyChange = output<boolean>();
  readonly proposedChange = output<ProposedBlock | null>();

  private readonly loads = new Subject<string>();
  private readonly evaluations = new Subject<{
    readonly visitId: string;
    readonly body: EvaluationInput | null;
  }>();

  readonly titleId = 'dispatch-drawer-title';
  readonly controlIds = CONTROL_IDS;
  readonly open = computed(() => this.visitId() !== null);
  readonly maxTechnicians = MAX_TECHNICIANS;
  readonly noteMax = MAX_NOTE;
  readonly reasonMax = MAX_REASON;
  readonly smsMessage = SMS_UNAVAILABLE_MESSAGE;
  readonly noEmailMessage = NO_EMAIL_MESSAGE;
  readonly needsScheduleMessage = NEEDS_SCHEDULE_MESSAGE;
  readonly noTechniciansMessage = NO_TECHNICIANS_MESSAGE;
  readonly lockedMessage = LOCKED_MESSAGE;
  readonly changedMessage = CHANGED_MESSAGE;
  readonly saveFailedMessage = SAVE_FAILED_MESSAGE;
  readonly loadFailedMessage = LOAD_FAILED_MESSAGE;
  readonly evaluationFailedMessage = EVALUATION_FAILED_MESSAGE;
  readonly statusLabel = visitStatusLabel;
  readonly impactIcons = ['pi-briefcase', 'pi-arrow-left', 'pi-arrow-right'] as const;
  readonly priorityText = PRIORITY_TEXT;
  readonly priorityIcons = PRIORITY_ICONS;
  readonly skeletons = [0, 1, 2, 3];

  readonly loadState = signal<LoadState>('loading');
  readonly detail = signal<VisitDispatchDetail | null>(null);
  readonly form = signal<FormState | null>(null);
  private readonly initial = signal<string | null>(null);
  readonly errors = signal<Readonly<Partial<Record<FieldKey, string>>>>({});
  readonly saving = signal(false);
  readonly saveProblem = signal<SaveProblem>(null);
  private readonly serverConflicts = signal(false);

  readonly evaluation = signal<VisitEvaluation | null>(null);
  readonly evaluating = signal(false);
  readonly evaluationFailed = signal(false);

  readonly lockedLocally = signal(false);
  readonly readOnly = computed(() => {
    const detail = this.detail();
    return detail === null || !detail.canManage || detail.isLocked || this.lockedLocally();
  });
  readonly showLockedNote = computed(() => {
    const detail = this.detail();
    return (detail?.isLocked ?? false) || this.lockedLocally();
  });
  readonly isDirty = computed(() => {
    const form = this.form();
    const initial = this.initial();
    return form !== null && initial !== null && JSON.stringify(form) !== initial;
  });

  readonly hasSchedule = computed(() => {
    const form = this.form();
    return !!form && form.date !== '' && !!form.start && !!form.end;
  });
  readonly timeOptions = computed(() => ({
    start: timeChoices(this.form()?.start ?? null),
    end: timeChoices(this.form()?.end ?? null),
  }));
  readonly arrivalOptions = computed(() => arrivalChoices(this.form()?.start ?? null));
  readonly technicians = computed(() => [...(this.detail()?.technicians ?? [])]);
  readonly selectedTechnicians = computed<readonly DispatchTechnician[]>(() => {
    const ids = this.form()?.technicianIds ?? [];
    const all = this.technicians();
    return ids
      .map((id) => all.find((technician) => technician.id === id))
      .filter((technician): technician is DispatchTechnician => technician !== undefined);
  });
  readonly bestMatchId = computed(
    () => this.evaluation()?.ranking.find((entry) => entry.bestMatch)?.technicianId ?? null,
  );
  readonly conflictCount = computed(() => this.evaluation()?.conflicts.length ?? 0);
  readonly reasonNeeded = computed(() => this.conflictCount() > 0 || this.serverConflicts());
  readonly bannerText = computed(() => {
    const count = this.conflictCount();
    return count === 0 ? 'No scheduling conflicts' : `${count} scheduling conflicts`;
  });
  readonly emailDisabled = computed(
    () => this.readOnly() || this.saving() || !(this.detail()?.customer.hasEmail ?? false),
  );
  readonly willNotify = computed(() => {
    const detail = this.detail();
    const form = this.form();
    if (detail === null || form === null || !form.notify || !detail.customer.hasEmail) {
      return false;
    }
    const before = detail.values;
    const scheduleChanged =
      detail.status === 'unscheduled' ||
      form.date !== (before.date ?? '') ||
      form.start !== before.start ||
      form.end !== before.end ||
      form.arrivalWindow !== before.arrivalWindow;
    const previous = new Set(before.technicianIds);
    const techniciansChanged =
      previous.size !== form.technicianIds.length ||
      form.technicianIds.some((id) => !previous.has(id));
    return (
      scheduleChanged || (techniciansChanged && form.technicianIds.length > 0 && form.sendDetails)
    );
  });
  readonly primaryLabel = computed(() => {
    const detail = this.detail();
    const form = this.form();
    const base =
      detail !== null && detail.status !== 'unscheduled'
        ? 'Update'
        : (form?.technicianIds.length ?? 0) > 0
          ? 'Assign'
          : 'Schedule';
    return this.willNotify() ? `${base} & notify` : base;
  });
  readonly announcement = computed(() => {
    if (this.evaluating() || this.evaluation() === null) {
      return '';
    }
    return this.bannerText();
  });

  constructor() {
    this.loads
      .pipe(
        switchMap((id) =>
          this.api.visit(id).pipe(
            map((detail): LoadResult => ({ ok: true, detail })),
            catchError((error: unknown) =>
              of<LoadResult>({
                ok: false,
                notFound: isApiError(error) && error.kind === 'not-found',
              }),
            ),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.handleLoad(result));

    this.evaluations
      .pipe(
        debounceTime(EVALUATION_DEBOUNCE_MS),
        switchMap(({ visitId, body }) =>
          body === null
            ? of<EvaluationResult | null>(null)
            : this.api.evaluate(visitId, body).pipe(
                map((evaluation): EvaluationResult => ({ ok: true, evaluation })),
                catchError((error: unknown) =>
                  of<EvaluationResult>({
                    ok: false,
                    fieldErrors: isApiError(error) ? error.fieldErrors : {},
                    locked: isApiError(error) && error.code === 'visit_locked',
                  }),
                ),
              ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.handleEvaluation(result));

    effect(() => {
      const id = this.visitId();
      untracked(() => {
        this.resetState();
        if (id !== null) {
          this.loads.next(id);
        }
      });
    });
    effect(() => this.dirtyChange.emit(this.isDirty()));
    effect(() => {
      const form = this.form();
      this.proposedChange.emit(
        form === null
          ? null
          : {
              date: form.date === '' ? null : form.date,
              start: form.start,
              end: form.end,
              technicianIds: form.technicianIds,
            },
      );
    });
  }

  /** Close and Cancel are ignored while the single save request is pending. */
  requestClose(): void {
    if (!this.saving()) {
      this.closeRequested.emit();
    }
  }

  // Loading

  reload(): void {
    const id = this.visitId();
    if (id !== null) {
      this.resetState();
      this.loads.next(id);
    }
  }

  private resetState(): void {
    this.loadState.set('loading');
    this.detail.set(null);
    this.form.set(null);
    this.initial.set(null);
    this.errors.set({});
    this.saveProblem.set(null);
    this.saving.set(false);
    this.serverConflicts.set(false);
    this.lockedLocally.set(false);
    this.evaluation.set(null);
    this.evaluating.set(false);
    this.evaluationFailed.set(false);
  }

  private handleLoad(result: LoadResult): void {
    if (!result.ok) {
      if (result.notFound) {
        this.unavailable.emit();
        return;
      }
      this.loadState.set('error');
      return;
    }
    const form = formFrom(result.detail);
    this.detail.set(result.detail);
    this.form.set(form);
    this.initial.set(JSON.stringify(form));
    this.loadState.set('ready');
    this.queueEvaluation();
  }

  // Form changes

  patch(changes: Partial<FormState>, field: FieldKey | null = null): void {
    const current = this.form();
    if (current === null) {
      return;
    }
    this.form.set({ ...current, ...changes });
    if (field !== null) {
      this.clearError(field);
    }
    if (
      'date' in changes ||
      'start' in changes ||
      'end' in changes ||
      'technicianIds' in changes ||
      'primaryId' in changes
    ) {
      this.serverConflicts.set(false);
      this.queueEvaluation();
    }
  }

  setTechnicians(ids: readonly string[] | null): void {
    const next = (ids ?? []).slice(0, MAX_TECHNICIANS);
    const primary = this.form()?.primaryId ?? null;
    this.patch(
      {
        technicianIds: next,
        primaryId: next.length === 0 ? null : next.includes(primary ?? '') ? primary : next[0],
      },
      'technicianIds',
    );
    this.clearError('primaryTechnicianId');
  }

  private clearError(field: FieldKey): void {
    if (this.errors()[field] !== undefined) {
      this.errors.set(
        Object.fromEntries(Object.entries(this.errors()).filter(([key]) => key !== field)),
      );
    }
  }

  // Evaluation (BR-09, BR-15)

  private evaluationBody(): EvaluationInput | null {
    const form = this.form();
    if (
      form === null ||
      form.date === '' ||
      !form.start ||
      !form.end ||
      clockMinutes(form.end) <= clockMinutes(form.start)
    ) {
      return null;
    }
    return {
      date: form.date,
      start: form.start,
      end: form.end,
      technicianIds: form.technicianIds,
      primaryTechnicianId: form.primaryId,
    };
  }

  queueEvaluation(): void {
    const detail = this.detail();
    if (detail === null || this.readOnly()) {
      return;
    }
    const body = this.evaluationBody();
    this.evaluationFailed.set(false);
    this.evaluating.set(body !== null);
    if (body === null) {
      this.evaluation.set(null);
    }
    this.evaluations.next({ visitId: detail.visitId, body });
  }

  private handleEvaluation(result: EvaluationResult | null): void {
    this.evaluating.set(false);
    if (result === null) {
      return;
    }
    if (result.ok) {
      this.evaluation.set(result.evaluation);
      this.evaluationFailed.set(false);
      return;
    }
    if (result.locked) {
      this.lockedLocally.set(true);
      return;
    }
    if (Object.keys(result.fieldErrors).length > 0) {
      this.errors.set({ ...this.errors(), ...this.mapServerErrors(result.fieldErrors) });
      this.evaluation.set(null);
      return;
    }
    this.evaluationFailed.set(true);
  }

  // Display helpers

  checksFor(technicianId: string): readonly Check[] {
    const entry = this.evaluation()?.checks.find((check) => check.technicianId === technicianId);
    return entry === undefined ? [] : [entry.availability, entry.overlap];
  }

  impactLines(): readonly { readonly name: string; readonly lines: readonly string[] }[] {
    const evaluation = this.evaluation();
    if (evaluation === null) {
      return [];
    }
    return this.selectedTechnicians().flatMap((technician) => {
      const impact = evaluation.impact.find((entry) => entry.technicianId === technician.id);
      return impact === undefined
        ? []
        : [
            {
              name: technician.name,
              lines: [impactText(impact), previousText(impact.previous), nextText(impact.next)],
            },
          ];
    });
  }

  // Save (BR-14, BR-20)

  submit(): void {
    const detail = this.detail();
    const form = this.form();
    if (detail === null || form === null || this.readOnly() || this.saving()) {
      return;
    }
    const errors = this.validate(form);
    if (Object.keys(errors).length > 0) {
      this.errors.set(errors);
      this.focusFirst(errors);
      return;
    }
    this.errors.set({});
    this.saveProblem.set(null);
    this.saving.set(true);
    const body: DispatchBody = {
      date: form.date,
      start: form.start as string,
      end: form.end as string,
      arrivalWindow: form.arrivalWindow,
      technicianIds: form.technicianIds,
      primaryTechnicianId: form.technicianIds.length === 0 ? null : form.primaryId,
      dispatchNote: form.note.trim() === '' ? null : form.note.trim(),
      notifyCustomer: form.notify,
      sendTechnicianDetails: form.sendDetails,
      overrideReason: this.reasonNeeded() ? form.reason.trim() : null,
      updatedAt: detail.updatedAt,
    };
    this.api.dispatch(detail.visitId, body).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.saved.emit({ result, message: this.toast(detail, result) });
      },
      error: (error: unknown) => this.handleSaveError(error),
    });
  }

  private toast(detail: VisitDispatchDetail, result: VisitDispatchResult): string {
    const base =
      detail.status !== 'unscheduled'
        ? 'Visit updated.'
        : result.visit.status === 'assigned'
          ? 'Visit assigned.'
          : 'Visit scheduled.';
    return result.notified ? `${base} Customer notified by email.` : base;
  }

  private validate(form: FormState): Partial<Record<FieldKey, string>> {
    const errors: Partial<Record<FieldKey, string>> = {};
    if (form.date === '') {
      errors.date = MESSAGES.date;
    }
    if (!form.start) {
      errors.start = MESSAGES.start;
    }
    if (!form.end) {
      errors.end = MESSAGES.end;
    } else if (form.start && clockMinutes(form.end) <= clockMinutes(form.start)) {
      errors.end = MESSAGES.endOrder;
    }
    if (form.technicianIds.length > MAX_TECHNICIANS) {
      errors.technicianIds = MESSAGES.technicians;
    }
    if (
      form.technicianIds.length > 0 &&
      (form.primaryId === null || !form.technicianIds.includes(form.primaryId))
    ) {
      errors.primaryTechnicianId = MESSAGES.primary;
    }
    if (form.note.trim().length > MAX_NOTE) {
      errors.dispatchNote = MESSAGES.note;
    }
    if (this.reasonNeeded()) {
      const length = form.reason.trim().length;
      if (length === 0) {
        errors.overrideReason = MESSAGES.reasonRequired;
      } else if (length < MIN_REASON) {
        errors.overrideReason = MESSAGES.reasonShort;
      } else if (length > MAX_REASON) {
        errors.overrideReason = MESSAGES.reasonLong;
      }
    }
    return errors;
  }

  private handleSaveError(error: unknown): void {
    this.saving.set(false);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const mapped = this.mapServerErrors(apiError.fieldErrors);
      if (Object.keys(mapped).length > 0) {
        this.errors.set(mapped);
        this.focusFirst(mapped);
        return;
      }
    }
    if (apiError?.kind === 'conflict') {
      if (apiError.code === 'scheduling_conflicts') {
        this.serverConflicts.set(true);
        this.errors.set({ overrideReason: MESSAGES.reasonRequired });
        this.queueEvaluation();
        this.focusFirst({ overrideReason: MESSAGES.reasonRequired });
        return;
      }
      if (apiError.code === 'visit_locked') {
        this.lockedLocally.set(true);
        this.saveProblem.set('locked');
        return;
      }
      if (apiError.code === 'visit_changed') {
        this.saveProblem.set('changed');
        return;
      }
    }
    this.saveProblem.set('failed');
  }

  private mapServerErrors(
    fieldErrors: Readonly<Record<string, readonly string[]>>,
  ): Partial<Record<FieldKey, string>> {
    const mapped: Partial<Record<FieldKey, string>> = {};
    for (const [key, messages] of Object.entries(fieldErrors)) {
      const last = (key.split('.').pop() ?? key).toLowerCase();
      const field = FOCUS_ORDER.find((candidate) => candidate.toLowerCase() === last);
      if (field !== undefined && messages[0] !== undefined && mapped[field] === undefined) {
        mapped[field] = messages[0];
      }
    }
    return mapped;
  }

  private focusFirst(errors: Partial<Record<FieldKey, string>>): void {
    const first = FOCUS_ORDER.find((field) => errors[field] !== undefined);
    if (first !== undefined) {
      this.focus(CONTROL_IDS[first]);
    }
  }

  private focus(id: string): void {
    afterNextRender(() => this.document.getElementById(id)?.focus(), {
      injector: this.injector,
    });
  }

  describedBy(field: FieldKey): string | null {
    return this.errors()[field] === undefined ? null : `${CONTROL_IDS[field]}-error`;
  }
}
