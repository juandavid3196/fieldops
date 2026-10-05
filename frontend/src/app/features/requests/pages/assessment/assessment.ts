import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { Observable, Subject, catchError, forkJoin, map, of, switchMap } from 'rxjs';

import { MD_QUERY, watchMedia } from '../../../../core/config/breakpoints';
import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { addDays, todayInTimeZone } from '../../../service-request/service-request.validators';
import { AssessmentCalendarView } from '../../components/assessment-calendar/assessment-calendar';
import { AssessmentForm } from '../../components/assessment-form/assessment-form';
import { AssessmentTechnicianPicker } from '../../components/assessment-technician-picker/assessment-technician-picker';
import {
  AssessmentBody,
  AssessmentCalendar,
  AssessmentPlanner,
  CONFLICT_MESSAGE,
  MANAGER_ROLES,
  NO_EMAIL_MESSAGE,
  PlannerQuery,
  REQUEST_ID_PATTERN,
  RequestDetail,
  RequestOptions,
  SAVE_FAILED_MESSAGE,
  STATUS_LABELS,
  TOAST_STATE_KEY,
  ToastHandoff,
  UNAVAILABLE_MESSAGE,
} from '../../models/requests.model';
import { AssessmentPlannerService } from '../../services/assessment-planner.service';
import { RequestsService } from '../../services/requests.service';
import { CalendarView, ProposedSlot, shiftDate, weekStart } from '../../utils/assessment-calendar';
import {
  AssessmentFormValue,
  AssessmentMode,
  Option,
  defaultFormValue,
  hasRecipientEmail,
  isLocalDate,
  minutesOf,
  prefilledFormValue,
  previewMessage,
  slotEnd,
  slotStart,
} from '../../utils/assessment-schedule';
import { addressLine, fieldKey, preferredVisit } from '../../utils/requests-format';

export const FORBIDDEN_MESSAGE = "You don't have access to schedule assessments.";
export const NOT_SCHEDULABLE_MESSAGE = "This request can't have an assessment scheduled.";
export const LOAD_ERROR_MESSAGE = "We couldn't load this request.";
export const SUBTITLE =
  'Arrange an optional site visit to gather information before preparing a quote.';
export const NO_PREFERRED_MESSAGE = 'No preferred times provided.';
export const PREFERRED_NOTE =
  'These are preferred times, not a confirmed appointment until you schedule the assessment.';
export const FOOTER_NOTE =
  'Completing the assessment returns the request to Ready for quote. A quote must still be created and sent separately.';
export const TECHNICIAN_REQUIRED_MESSAGE = 'Choose a technician.';
export const PURPOSE_REQUIRED_MESSAGE = 'Enter the purpose of the assessment.';
/** Fixed copy of the `409` conflict codes (the backend title is never displayed, BR-09). */
export const CONFLICT_COPY: Readonly<Record<string, string>> = {
  technician_conflict: 'This technician already has a commitment at that time.',
  technician_time_off: 'This technician is off at that time.',
};

const STEPS = [
  'Request under review',
  'Assessment scheduled',
  'Assessment completed',
  'Ready for quote',
] as const;

/** Server field keys to the form control that owns them. */
const FIELD_OWNER: Readonly<Record<string, string>> = {
  start: 'window',
  end: 'duration',
  notifyCustomer: 'notifyCustomer',
};

type PageState = 'loading' | 'ready' | 'forbidden' | 'not-available' | 'not-schedulable' | 'error';

type LoadResult =
  | { readonly kind: 'ready'; readonly detail: RequestDetail; readonly options: RequestOptions }
  | { readonly kind: 'state'; readonly state: PageState }
  | { readonly kind: 'unauthorized' };

type PlannerResult =
  | { readonly ok: true; readonly planner: AssessmentPlanner }
  | { readonly ok: false; readonly error: ApiError | null };

type CalendarResult =
  | { readonly ok: true; readonly calendar: AssessmentCalendar | null }
  | { readonly ok: false; readonly error: ApiError | null };

interface CalendarRequest {
  readonly technicianId: string;
  readonly from: string;
  readonly to: string;
}

interface Step {
  readonly label: string;
  readonly status: 'done' | 'current' | 'upcoming';
}

/**
 * Schedule assessment page (`/requests/:requestId/assessment`, Design 2). Single owner of the
 * page state: request, planner, calendar, form value, validation and submission. Managers only
 * (checked before any call); schedule or reschedule mode follows the request status (BR-01).
 */
@Component({
  selector: 'app-assessment',
  imports: [
    RouterLink,
    ButtonDirective,
    ConfirmDialog,
    DiscardChangesDialog,
    Message,
    Skeleton,
    Toast,
    AssessmentCalendarView,
    AssessmentForm,
    AssessmentTechnicianPicker,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './assessment.html',
  styleUrl: './assessment.scss',
})
export class Assessment {
  private readonly requests = inject(RequestsService);
  private readonly planning = inject(AssessmentPlannerService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  readonly requestId = this.route.snapshot.paramMap.get('requestId') ?? '';
  readonly requestQueryParams = { request: this.requestId };
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly notSchedulableMessage = NOT_SCHEDULABLE_MESSAGE;
  readonly unavailableMessage = UNAVAILABLE_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly subtitle = SUBTITLE;
  readonly noPreferredMessage = NO_PREFERRED_MESSAGE;
  readonly preferredNote = PREFERRED_NOTE;
  readonly footerNote = FOOTER_NOTE;
  readonly noEmailMessage = NO_EMAIL_MESSAGE;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  /** UX only: the backend decides (BR-01). */
  readonly canManage = computed(() => MANAGER_ROLES.includes(this.roleCode()));
  readonly sessionExpired = signal(false);

  readonly state = signal<PageState>('loading');
  readonly detail = signal<RequestDetail | null>(null);
  readonly options = signal<RequestOptions | null>(null);
  readonly timezone = signal('UTC');

  readonly mode = computed<AssessmentMode>(() =>
    this.detail()?.status === 'assessment_scheduled' ? 'reschedule' : 'schedule',
  );
  readonly title = computed(() =>
    this.mode() === 'reschedule' ? 'Reschedule assessment' : 'Schedule assessment',
  );
  readonly statusChip = computed(() => {
    const detail = this.detail();
    return detail === null ? '' : `Request: ${STATUS_LABELS[detail.status]}`;
  });

  // Form
  readonly form = signal<AssessmentFormValue | null>(null);
  private readonly initialForm = signal('');
  readonly errors = signal<Readonly<Record<string, string>>>({});
  readonly submitting = signal(false);
  readonly dirty = computed(() => {
    const form = this.form();
    return form !== null && JSON.stringify(form) !== this.initialForm();
  });
  readonly today = computed(() => todayInTimeZone(this.timezone()));
  readonly needsBranch = computed(() => this.detail()?.branch === null);
  readonly emailAvailable = computed(() => {
    const detail = this.detail();
    return detail !== null && hasRecipientEmail(detail);
  });
  readonly branchOptions = computed<Option<string>[]>(() =>
    (this.options()?.branches ?? []).map((branch) => ({ code: branch.id, label: branch.name })),
  );

  // Summary
  readonly summary = computed(() => {
    const detail = this.detail();
    if (detail === null) {
      return null;
    }
    return {
      number: detail.number,
      customer: detail.customer?.name ?? detail.contact.name ?? 'Guest',
      property: addressLine(detail.serviceAddress) ?? '—',
      issue: detail.title,
    };
  });
  readonly preferred = computed(() => {
    const availability = this.detail()?.availability ?? null;
    return { visit: preferredVisit(availability), notes: availability?.schedulingNotes ?? null };
  });
  readonly steps = computed<Step[]>(() => {
    const current = this.mode() === 'reschedule' ? 2 : 1;
    return STEPS.map((label, index) => ({
      label,
      status: index < current ? 'done' : index === current ? 'current' : 'upcoming',
    }));
  });

  // Planner
  readonly planner = signal<AssessmentPlanner | null>(null);
  readonly plannerLoading = signal(false);
  readonly plannerFailed = signal(false);
  readonly waitingForBranch = computed(() => {
    const form = this.form();
    return this.needsBranch() && (form?.branchId ?? '') === '';
  });
  readonly technicianOptions = computed<Option<string>[]>(() =>
    (this.planner()?.technicians ?? []).map((technician) => ({
      code: technician.id,
      label: technician.name,
    })),
  );
  readonly plannerZone = computed(() => this.planner()?.timezone ?? this.timezone());
  private readonly technicianName = computed(() => {
    const id = this.form()?.technicianId ?? '';
    if (id === '') {
      return null;
    }
    const listed = this.planner()?.technicians.find((technician) => technician.id === id);
    const current = this.detail()?.assessment?.technician;
    return listed?.name ?? (current?.id === id ? current.name : null);
  });
  readonly preview = computed(() => {
    const form = this.form();
    return form === null ? null : previewMessage(form, this.technicianName(), this.mode());
  });

  // Calendar
  readonly calendar = signal<AssessmentCalendar | null>(null);
  readonly calendarLoading = signal(false);
  readonly calendarFailed = signal(false);
  readonly calendarView = signal<CalendarView>('week');
  readonly calendarDate = signal('');
  readonly proposed = computed<ProposedSlot | null>(() => {
    const form = this.form();
    return form !== null && isLocalDate(form.date) && form.window !== ''
      ? { date: form.date, startMinutes: minutesOf(form.window), durationMinutes: form.duration }
      : null;
  });
  readonly calendarZone = computed(() => this.calendar()?.timezone ?? this.timezone());

  // Cancel assessment confirm (BR-15)
  readonly cancelNotify = signal(false);

  private readonly loads = new Subject<void>();
  private readonly plannerLoads = new Subject<PlannerQuery>();
  private readonly calendarLoads = new Subject<CalendarRequest | null>();
  private lastCalendarKey = '';
  /** The discard prompt (or a deliberate leave) already ran: the guard lets the route go. */
  private leaveConfirmed = false;

  constructor() {
    // Mobile opens the calendar in Day view (spec: responsive).
    const media = watchMedia(MD_QUERY, () => undefined);
    media.stop();
    if (!media.matches) {
      this.calendarView.set('day');
    }

    this.loads
      .pipe(
        switchMap(() => this.loadRequest$()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleLoad(result));

    this.plannerLoads
      .pipe(
        switchMap((query) => {
          this.plannerLoading.set(true);
          return this.planning.getPlanner(this.requestId, query).pipe(
            map((planner): PlannerResult => ({ ok: true, planner })),
            catchError((error: unknown) =>
              of<PlannerResult>({ ok: false, error: isApiError(error) ? error : null }),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handlePlanner(result));

    this.calendarLoads
      .pipe(
        switchMap((query) => {
          if (query === null) {
            return of<CalendarResult>({ ok: true, calendar: null });
          }
          this.calendarLoading.set(true);
          return this.planning.getCalendar(this.requestId, query).pipe(
            map((calendar): CalendarResult => ({ ok: true, calendar })),
            catchError((error: unknown) =>
              of<CalendarResult>({ ok: false, error: isApiError(error) ? error : null }),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleCalendar(result));

    // The role is checked before any call (BR-01); an invalid id never reaches the API.
    if (!this.canManage()) {
      this.settle('forbidden');
    } else if (!REQUEST_ID_PATTERN.test(this.requestId)) {
      this.settle('not-available');
    } else {
      this.loads.next();
    }
  }

  // Loading

  private loadRequest$(): Observable<LoadResult> {
    this.state.set('loading');
    return forkJoin({
      detail: this.requests.detail(this.requestId),
      options: this.requests.options(),
    }).pipe(
      map(({ detail, options }): LoadResult => ({ kind: 'ready', detail, options })),
      catchError((error: unknown) => {
        const apiError = isApiError(error) ? error : null;
        switch (apiError?.kind) {
          case 'unauthorized':
            return of<LoadResult>({ kind: 'unauthorized' });
          case 'forbidden':
            return of<LoadResult>({ kind: 'state', state: 'forbidden' });
          case 'not-found':
            return of<LoadResult>({ kind: 'state', state: 'not-available' });
          default:
            return of<LoadResult>({ kind: 'state', state: 'error' });
        }
      }),
    );
  }

  retryLoad(): void {
    this.loads.next();
  }

  private handleLoad(result: LoadResult): void {
    if (result.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    if (result.kind === 'state') {
      this.settle(result.state);
      return;
    }
    const { detail, options } = result;
    this.detail.set(detail);
    this.options.set(options);
    this.timezone.set(options.timezone);
    if (
      detail.status !== 'new' &&
      detail.status !== 'needs_review' &&
      detail.status !== 'assessment_scheduled'
    ) {
      this.settle('not-schedulable');
      return;
    }
    const form =
      detail.status === 'assessment_scheduled'
        ? prefilledFormValue(detail, options.timezone)
        : defaultFormValue(detail, options.timezone);
    this.form.set(form);
    this.initialForm.set(JSON.stringify(form));
    this.errors.set({});
    this.calendarDate.set(form.date);
    this.settle('ready');
    this.loadPlanner();
    this.refreshCalendar(true);
  }

  private settle(state: PageState): void {
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  // Planner and calendar

  loadPlanner(): void {
    const form = this.form();
    if (
      form === null ||
      this.waitingForBranch() ||
      !isLocalDate(form.date) ||
      form.date < this.today()
    ) {
      return;
    }
    this.plannerFailed.set(false);
    this.plannerLoads.next({
      date: form.date,
      start: form.window,
      durationMinutes: form.duration,
      ...(this.needsBranch() ? { branchId: form.branchId } : {}),
    });
  }

  private handlePlanner(result: PlannerResult): void {
    this.plannerLoading.set(false);
    if (!result.ok) {
      if (!this.handleCommonFailure(result.error)) {
        this.plannerFailed.set(true);
      }
      return;
    }
    this.planner.set(result.planner);
    const form = this.form();
    if (
      form !== null &&
      form.technicianId !== '' &&
      !result.planner.technicians.some((technician) => technician.id === form.technicianId)
    ) {
      this.form.set({ ...form, technicianId: '' });
      this.refreshCalendar();
    }
  }

  /** The calendar follows the technician and the week of `calendarDate`; unchanged keys are skipped. */
  refreshCalendar(force = false): void {
    const form = this.form();
    const technicianId = form?.technicianId ?? '';
    const date = this.calendarDate();
    if (form === null || technicianId === '' || !isLocalDate(date)) {
      this.lastCalendarKey = '';
      this.calendarFailed.set(false);
      this.calendarLoading.set(false);
      this.calendarLoads.next(null);
      return;
    }
    const from = weekStart(date);
    const key = `${technicianId}|${from}`;
    if (!force && key === this.lastCalendarKey && !this.calendarFailed()) {
      return;
    }
    this.lastCalendarKey = key;
    this.calendarFailed.set(false);
    this.calendarLoads.next({ technicianId, from, to: addDays(from, 6) });
  }

  private handleCalendar(result: CalendarResult): void {
    this.calendarLoading.set(false);
    if (!result.ok) {
      if (!this.handleCommonFailure(result.error)) {
        this.calendarFailed.set(true);
      }
      return;
    }
    this.calendar.set(result.calendar);
  }

  /** Session, permission, missing and changed-request failures shared by every call. */
  private handleCommonFailure(error: ApiError | null): boolean {
    switch (error?.kind) {
      case 'unauthorized':
        this.onUnauthorized();
        return true;
      case 'forbidden':
        this.settle('forbidden');
        return true;
      case 'not-found':
        this.settle('not-available');
        return true;
      case 'conflict':
        this.leaveToRequest({ severity: 'error', summary: CONFLICT_MESSAGE });
        return true;
      default:
        return false;
    }
  }

  // Form and calendar interaction

  onFormChange(next: AssessmentFormValue): void {
    const previous = this.form();
    if (previous === null) {
      return;
    }
    const slotChanged =
      next.date !== previous.date ||
      next.window !== previous.window ||
      next.duration !== previous.duration ||
      next.branchId !== previous.branchId;
    this.form.set(next);
    this.clearErrors(previous, next);
    if (next.branchId !== previous.branchId) {
      this.planner.set(null);
      this.calendar.set(null);
    }
    if (next.date !== previous.date && isLocalDate(next.date)) {
      this.calendarDate.set(next.date);
    }
    if (slotChanged) {
      this.loadPlanner();
    }
    if (next.technicianId !== previous.technicianId || next.date !== previous.date) {
      this.refreshCalendar();
    }
  }

  selectTechnician(id: string): void {
    const form = this.form();
    if (form !== null && !this.submitting()) {
      this.onFormChange({ ...form, technicianId: id });
    }
  }

  /** Editing a field drops its inline error; any slot or technician change drops a conflict. */
  private clearErrors(previous: AssessmentFormValue, next: AssessmentFormValue): void {
    const cleared: string[] = [];
    if (next.branchId !== previous.branchId) {
      cleared.push('branchId');
    }
    if (next.date !== previous.date) {
      cleared.push('date');
    }
    if (next.window !== previous.window) {
      cleared.push('window');
    }
    if (next.duration !== previous.duration) {
      cleared.push('duration');
    }
    if (next.purpose !== previous.purpose) {
      cleared.push('purpose');
    }
    if (next.internalInstructions !== previous.internalInstructions) {
      cleared.push('internalInstructions');
    }
    if (next.notify !== previous.notify) {
      cleared.push('notifyCustomer');
    }
    if (next.technicianId !== previous.technicianId) {
      cleared.push('technicianId');
    }
    if (
      next.date !== previous.date ||
      next.window !== previous.window ||
      next.duration !== previous.duration
    ) {
      cleared.push('technicianId');
    }
    if (cleared.some((field) => field in this.errors())) {
      this.errors.update((errors) =>
        Object.fromEntries(Object.entries(errors).filter(([field]) => !cleared.includes(field))),
      );
    }
  }

  onCalendarView(view: CalendarView): void {
    this.calendarView.set(view);
  }

  onCalendarShift(direction: -1 | 1): void {
    this.calendarDate.set(shiftDate(this.calendarView(), this.calendarDate(), direction));
    this.refreshCalendar();
  }

  // Submit (BR-18)

  submit(): void {
    const form = this.form();
    const detail = this.detail();
    if (form === null || detail === null || this.submitting()) {
      return;
    }
    const errors = this.validate(form);
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }
    const body: AssessmentBody = {
      start: slotStart(form),
      end: slotEnd(form),
      technicianId: form.technicianId,
      ...(this.needsBranch() ? { branchId: form.branchId } : {}),
      purpose: form.purpose.trim(),
      internalInstructions: form.internalInstructions.trim() || null,
      notifyCustomer: form.notify && this.emailAvailable(),
    };
    const rescheduling = this.mode() === 'reschedule';
    this.run(
      rescheduling
        ? this.requests.rescheduleAssessment(this.requestId, body)
        : this.requests.scheduleAssessment(this.requestId, body),
      rescheduling ? 'Assessment rescheduled.' : 'Assessment scheduled.',
    );
  }

  private validate(form: AssessmentFormValue): Record<string, string> {
    const errors: Record<string, string> = {};
    if (this.needsBranch() && form.branchId === '') {
      errors['branchId'] = 'Choose a branch.';
    }
    if (!isLocalDate(form.date)) {
      errors['date'] = 'Choose a date.';
    } else if (form.date < this.today()) {
      errors['date'] = 'Choose a date from today onward.';
    }
    if (form.technicianId === '') {
      errors['technicianId'] = TECHNICIAN_REQUIRED_MESSAGE;
    }
    const purpose = form.purpose.trim();
    if (purpose.length === 0) {
      errors['purpose'] = PURPOSE_REQUIRED_MESSAGE;
    } else if (purpose.length > 500) {
      errors['purpose'] = 'Purpose must be 500 characters or fewer.';
    }
    if (form.internalInstructions.trim().length > 2000) {
      errors['internalInstructions'] = 'Internal instructions must be 2000 characters or fewer.';
    }
    return errors;
  }

  // Cancel assessment (BR-15)

  confirmCancelAssessment(): void {
    if (this.submitting() || this.detail() === null) {
      return;
    }
    const notify = this.emailAvailable();
    this.cancelNotify.set(notify);
    this.confirmationService.confirm({
      header: 'Cancel assessment?',
      message: 'The request returns to Needs review.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Cancel assessment', severity: 'danger' },
      rejectButtonProps: { label: 'Keep', severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.requests.cancelAssessment(this.requestId, notify && this.cancelNotify()),
          'Assessment cancelled.',
        ),
    });
  }

  // Mutations

  private run(request$: Observable<RequestDetail>, success: string): void {
    if (this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.errors.set({});
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.leaveToRequest({ severity: 'success', summary: success }),
      error: (error: unknown) => this.mutationFailed(isApiError(error) ? error : null),
    });
  }

  private mutationFailed(error: ApiError | null): void {
    this.submitting.set(false);
    if (error?.kind === 'conflict') {
      const copy = error.code === undefined ? undefined : CONFLICT_COPY[error.code];
      if (copy !== undefined) {
        this.errors.set({ technicianId: copy });
        this.loadPlanner();
        this.refreshCalendar(true);
        return;
      }
    }
    if (this.handleCommonFailure(error)) {
      return;
    }
    if (
      (error?.kind === 'validation' || error?.kind === 'bad-request') &&
      Object.keys(error.fieldErrors).length > 0
    ) {
      this.errors.set(
        Object.fromEntries(
          Object.entries(error.fieldErrors).map(([path, messages]) => {
            const key = fieldKey(path);
            return [FIELD_OWNER[key] ?? key, messages[0]];
          }),
        ),
      );
      return;
    }
    this.messageService.add({ severity: 'error', summary: SAVE_FAILED_MESSAGE });
  }

  // Navigation

  /**
   * Back to the request with a toast handed over through the navigation state (the board owns the
   * toast outlet). The discard prompt is skipped: the edit was saved, cancelled or is obsolete.
   */
  private leaveToRequest(toast: ToastHandoff): void {
    this.leaveConfirmed = true;
    void this.router
      .navigate(['/requests'], {
        queryParams: { request: this.requestId },
        state: { [TOAST_STATE_KEY]: toast },
      })
      .finally(() => (this.leaveConfirmed = false));
  }

  /** Cancel: leaves without saving; the guard asks first when the form changed (BR-18). */
  backToRequest(): void {
    if (!this.submitting()) {
      void this.router.navigate(['/requests'], { queryParams: this.requestQueryParams });
    }
  }

  /** Consulted by `assessmentUnsavedChangesGuard` on route leave (BR-18). */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired() || this.leaveConfirmed || !this.dirty()) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this assessment',
          accept: () => {
            subscriber.next(true);
            subscriber.complete();
          },
          reject: () => {
            subscriber.next(false);
            subscriber.complete();
          },
        }),
      );
    });
  }

  onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }
}
