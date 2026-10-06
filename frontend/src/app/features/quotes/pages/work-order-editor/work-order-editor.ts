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
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';
import { Toast } from 'primeng/toast';
import { Observable, Subject, catchError, map, of, switchMap } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { formatMoney } from '../../../customers/utils/customer-detail-format';
import {
  JobType,
  Priority,
  RecurrenceFrequency,
  WO_ALREADY_CREATED_MESSAGE,
  WO_CHANGED_MESSAGE,
  WO_CUSTOMER_REQUIRED_MESSAGE,
  WO_FORBIDDEN_MESSAGE,
  WO_LOAD_ERROR_MESSAGE,
  WO_NOT_APPROVED_MESSAGE,
  WO_NOT_AVAILABLE_MESSAGE,
  WO_REQUEST_CHANGED_MESSAGE,
  WO_SAVE_FAILED_MESSAGE,
  WORK_ORDER_MANAGE_ROLES,
  WorkOrderBody,
  WorkOrderEditor as WorkOrderEditorModel,
} from '../../../jobs/models/work-order.model';
import { WorkOrdersService } from '../../../jobs/services/work-orders.service';
import {
  DURATION_OPTIONS,
  FREQUENCY_LABELS,
  JOB_TYPE_LABELS,
  PRIORITY_LABELS,
  WINDOW_LABELS,
  durationLabel,
  optionsOf,
} from '../../../jobs/utils/work-order-format';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { TOAST_STATE_KEY, ToastHandoff } from '../../../requests/models/requests.model';
import { RequestsService } from '../../../requests/services/requests.service';
import { todayInTimeZone } from '../../../service-request/service-request.validators';
import { QUOTE_ID_PATTERN } from '../../models/quote.model';
import {
  MaterialSave,
  WorkOrderMaterials,
} from '../../components/work-order-materials/work-order-materials';
import { WorkOrderContext } from '../../components/work-order-context/work-order-context';
import {
  WorkOrderPhoto,
  WorkOrderPhotos,
} from '../../components/work-order-photos/work-order-photos';
import { WorkOrderTasks } from '../../components/work-order-tasks/work-order-tasks';
import { WorkOrderTemplateDialog } from '../../components/work-order-template-dialog/work-order-template-dialog';
import {
  INSTRUCTIONS_LIMIT,
  MaterialRow,
  WorkOrderForm,
  appendTemplateTasks,
  errorKey,
  formErrors,
  formFromValues,
  moveTask,
  taskName,
  toBody,
} from '../../utils/work-order-form';

type PageState =
  | 'loading'
  | 'ready'
  | 'forbidden'
  | 'not-available'
  | 'not-approved'
  | 'customer-required'
  | 'error';

type Banner = { readonly message: string; readonly reload: boolean } | null;

const MAX_PHOTOS = 6;
const WINDOW_OPTIONS = optionsOf(WINDOW_LABELS);
const FREQUENCY_OPTIONS = optionsOf(FREQUENCY_LABELS);
const JOB_TYPE_OPTIONS = optionsOf(JOB_TYPE_LABELS);
const PRIORITY_OPTIONS = optionsOf(PRIORITY_LABELS);
const ONE_TIME_RECURRENCE = [{ code: null, label: 'Does not repeat' }];

/**
 * Work order editor (`/quotes/:quoteId/work-order`, Design 5). Single owner of the form: job
 * details, tasks, materials, instructions, scheduling requirements and communication. Locked
 * context is read-only. Managers only (checked before any call); the backend revalidates.
 */
@Component({
  selector: 'app-work-order-editor',
  imports: [
    FormsModule,
    RouterLink,
    ButtonDirective,
    InputText,
    Message,
    MultiSelect,
    Select,
    Skeleton,
    SpinnerIcon,
    Textarea,
    Toast,
    ConfirmDialog,
    DiscardChangesDialog,
    WorkOrderContext,
    WorkOrderMaterials,
    WorkOrderPhotos,
    WorkOrderTasks,
    WorkOrderTemplateDialog,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './work-order-editor.html',
  styleUrl: './work-order-editor.scss',
})
export class WorkOrderEditor {
  private readonly workOrders = inject(WorkOrdersService);
  private readonly requests = inject(RequestsService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly quoteId = this.route.snapshot.paramMap.get('quoteId') ?? '';
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  readonly forbiddenMessage = WO_FORBIDDEN_MESSAGE;
  readonly notAvailableMessage = WO_NOT_AVAILABLE_MESSAGE;
  readonly notApprovedMessage = WO_NOT_APPROVED_MESSAGE;
  readonly customerRequiredMessage = WO_CUSTOMER_REQUIRED_MESSAGE;
  readonly loadErrorMessage = WO_LOAD_ERROR_MESSAGE;
  readonly instructionsLimit = INSTRUCTIONS_LIMIT;

  readonly jobTypes = JOB_TYPE_OPTIONS;
  readonly priorities = PRIORITY_OPTIONS;
  readonly durations = DURATION_OPTIONS;
  readonly windows = WINDOW_OPTIONS;
  readonly frequencies = FREQUENCY_OPTIONS;
  readonly oneTimeRecurrence = ONE_TIME_RECURRENCE;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  /** UX only: the backend decides (BR-01). */
  readonly canManage = computed(() => WORK_ORDER_MANAGE_ROLES.includes(this.roleCode()));
  readonly sessionExpired = signal(false);

  readonly state = signal<PageState>('loading');
  readonly editor = signal<WorkOrderEditorModel | null>(null);
  readonly form = signal<WorkOrderForm | null>(null);
  readonly errors = signal<Readonly<Record<string, string>>>({});
  readonly submitting = signal<'draft' | 'create' | null>(null);
  readonly banner = signal<Banner>(null);
  readonly announcement = signal('');
  readonly templatesOpen = signal(false);
  readonly photos = signal<readonly WorkOrderPhoto[]>([]);
  private readonly baseline = signal('');
  private photoUrls: string[] = [];

  readonly busy = computed(() => this.submitting() !== null);
  readonly dirty = computed(() => {
    const form = this.form();
    return form !== null && JSON.stringify(toBody(form)) !== this.baseline();
  });
  readonly saveState = computed(() => {
    if (this.editor() === null) {
      return '';
    }
    if (this.dirty()) {
      return 'Unsaved changes';
    }
    return this.editor()?.workOrder?.status === 'draft' ? 'Draft saved' : '';
  });
  readonly taskLabels = computed(() =>
    (this.form()?.tasks ?? []).map((task) => task.label.trim()).filter((label) => label.length > 0),
  );
  readonly approvedTotal = computed(() => {
    const quote = this.editor()?.quote;
    return quote === undefined ? '' : formatMoney(quote.approvedTotal, quote.currency);
  });
  readonly durationText = computed(() => durationLabel(this.form()?.durationMinutes ?? null));
  readonly today = computed(() => this.todayForBranch());

  private readonly loads = new Subject<void>();
  /** The discard prompt (or a deliberate leave) already ran: the guard lets the route go. */
  private leaveConfirmed = false;

  constructor() {
    this.loads
      .pipe(
        switchMap(() => this.load$()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleLoad(result));
    this.destroyRef.onDestroy(() => this.revokePhotos());

    // The role is checked before any call; an invalid id never reaches the API.
    if (!this.canManage()) {
      this.settle('forbidden');
    } else if (!QUOTE_ID_PATTERN.test(this.quoteId)) {
      this.settle('not-available');
    } else {
      this.loads.next();
    }
  }

  // Loading

  private load$(): Observable<
    | { readonly kind: 'ready'; readonly editor: WorkOrderEditorModel }
    | { readonly kind: 'state'; readonly state: PageState }
    | { readonly kind: 'unauthorized' }
  > {
    this.state.set('loading');
    return this.workOrders.editor(this.quoteId).pipe(
      map((editor) => ({ kind: 'ready' as const, editor })),
      catchError((error: unknown) => {
        const failure = isApiError(error) ? error : null;
        switch (failure?.kind) {
          case 'unauthorized':
            return of({ kind: 'unauthorized' as const });
          case 'forbidden':
            return of({ kind: 'state' as const, state: 'forbidden' as const });
          case 'not-found':
            return of({ kind: 'state' as const, state: 'not-available' as const });
          case 'conflict':
            return of({ kind: 'state' as const, state: this.blockedState(failure) ?? 'error' });
          default:
            return of({ kind: 'state' as const, state: 'error' as const });
        }
      }),
    );
  }

  private blockedState(error: ApiError): PageState | null {
    if (error.code === 'quote_not_approved') {
      return 'not-approved';
    }
    return error.code === 'customer_required' ? 'customer-required' : null;
  }

  retryLoad(): void {
    this.loads.next();
  }

  private handleLoad(
    result:
      | { readonly kind: 'ready'; readonly editor: WorkOrderEditorModel }
      | { readonly kind: 'state'; readonly state: PageState }
      | { readonly kind: 'unauthorized' },
  ): void {
    if (result.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    if (result.kind === 'state') {
      this.settle(result.state);
      return;
    }
    const { editor } = result;
    if (editor.workOrder !== null && editor.workOrder.status !== 'draft') {
      this.leaveConfirmed = true;
      void this.router.navigate(['/jobs', editor.workOrder.id], { replaceUrl: true });
      return;
    }
    const form = formFromValues(editor.values, () => crypto.randomUUID());
    this.editor.set(editor);
    this.form.set(form);
    this.baseline.set(JSON.stringify(toBody(form)));
    this.errors.set({});
    this.banner.set(null);
    this.loadPhotos(editor);
    this.settle('ready');
  }

  private settle(state: PageState): void {
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  private loadPhotos(editor: WorkOrderEditorModel): void {
    this.revokePhotos();
    const assessment = editor.assessment;
    const ids = (assessment?.photos ?? []).slice(0, MAX_PHOTOS).map((photo) => photo.id);
    this.photos.set(ids.map((id) => ({ id, state: 'loading', url: null })));
    if (assessment === null) {
      return;
    }
    ids.forEach((id) => {
      this.requests
        .downloadAssessmentPhoto(editor.requestId, assessment.id, id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (blob) => {
            const url = URL.createObjectURL(blob);
            this.photoUrls.push(url);
            this.patchPhoto(id, { state: 'ready', url });
          },
          error: () => this.patchPhoto(id, { state: 'failed', url: null }),
        });
    });
  }

  private patchPhoto(id: string, change: Pick<WorkOrderPhoto, 'state' | 'url'>): void {
    this.photos.update((items) => items.map((item) => (item.id === id ? { id, ...change } : item)));
  }

  private revokePhotos(): void {
    this.photoUrls.forEach((url) => URL.revokeObjectURL(url));
    this.photoUrls = [];
  }

  // Form changes

  private todayForBranch(): string {
    const editor = this.editor();
    const branchId = this.form()?.branchId;
    const zone =
      editor?.options.branches.find((branch) => branch.id === branchId)?.timezone ??
      editor?.organizationTimezone;
    return todayInTimeZone(zone ?? undefined);
  }

  private clearErrors(...keys: string[]): void {
    if (keys.some((key) => key in this.errors())) {
      this.errors.update((errors) =>
        Object.fromEntries(Object.entries(errors).filter(([key]) => !keys.includes(key))),
      );
    }
  }

  update(patch: Partial<WorkOrderForm>, ...keys: string[]): void {
    this.form.update((form) => (form === null ? form : { ...form, ...patch }));
    this.clearErrors(...keys);
  }

  onJobType(jobType: JobType): void {
    this.update(
      jobType === 'one_time' ? { jobType, frequency: null, occurrences: null } : { jobType },
      'recurrence',
    );
  }

  onFrequency(frequency: RecurrenceFrequency | null): void {
    this.update({ frequency }, 'recurrence');
  }

  onPriority(priority: Priority): void {
    this.update({ priority }, 'priority');
  }

  onPreferredDate(value: string): void {
    this.update(
      value === '' ? { preferredDate: '', arrivalWindow: 'any' } : { preferredDate: value },
      'preferredDate',
      'arrivalWindow',
    );
  }

  onCommunication(key: keyof WorkOrderForm['communication'], event: Event): void {
    const form = this.form();
    if (form !== null) {
      const checked = (event.target as HTMLInputElement).checked;
      this.update({ communication: { ...form.communication, [key]: checked } });
    }
  }

  // Tasks (BR-10, BR-11)

  onTaskLabel({ uid, label }: { readonly uid: string; readonly label: string }): void {
    const form = this.form();
    if (form !== null) {
      this.update(
        { tasks: form.tasks.map((task) => (task.uid === uid ? { ...task, label } : task)) },
        `tasks[${uid}]`,
        'tasks',
      );
    }
  }

  onAddTask(): void {
    const form = this.form();
    if (form === null) {
      return;
    }
    const uid = crypto.randomUUID();
    this.update({ tasks: [...form.tasks, { uid, label: '' }] }, 'tasks');
    this.focus(`[data-task-input][data-uid="${uid}"]`);
  }

  onRemoveTask(uid: string): void {
    const form = this.form();
    if (form === null) {
      return;
    }
    const index = form.tasks.findIndex((task) => task.uid === uid);
    const removed = form.tasks[index];
    const tasks = form.tasks.filter((task) => task.uid !== uid);
    this.update({ tasks });
    if (removed !== undefined) {
      this.announce(`Removed ${taskName(removed, index)}.`);
    }
    const next = tasks[index] ?? tasks[index - 1];
    this.focus(
      next === undefined ? '[data-field-key="tasks"]' : `[data-task-input][data-uid="${next.uid}"]`,
    );
  }

  /** Drag-and-drop reorder (BR-10): announced politely, focus moves to the moved row's input. */
  onDropTask({ from, to }: { readonly from: number; readonly to: number }): void {
    const move = moveTask(this.form()?.tasks ?? [], from, to);
    if (move === null) {
      return;
    }
    this.update({ tasks: move.tasks });
    const index = move.tasks.indexOf(move.task);
    this.announce(
      `${taskName(move.task, index)} moved to position ${move.position} of ${move.count}`,
    );
    this.focus(`[data-task-input][data-uid="${move.task.uid}"]`);
  }

  onTemplateApplied(labels: readonly string[]): void {
    const form = this.form();
    if (form === null) {
      return;
    }
    const tasks = appendTemplateTasks(form.tasks, labels, () => crypto.randomUUID());
    if (tasks === null) {
      return;
    }
    this.templatesOpen.set(false);
    this.update({ tasks }, 'tasks');
    this.announce(`Added ${labels.length} tasks from the template.`);
    const first = tasks[form.tasks.length];
    if (first !== undefined) {
      this.focus(`[data-task-input][data-uid="${first.uid}"]`);
    }
  }

  onTemplateSaved(): void {
    this.messageService.add({ severity: 'success', summary: 'Template saved.' });
  }

  // Materials (BR-12)

  onMaterialSaved({ uid, value }: MaterialSave): void {
    const form = this.form();
    if (form === null) {
      return;
    }
    const materials: MaterialRow[] =
      uid === null
        ? [...form.materials, { uid: crypto.randomUUID(), ...value }]
        : form.materials.map((material) => (material.uid === uid ? { uid, ...value } : material));
    this.update({ materials }, 'materials', ...(uid === null ? [] : [`materials[${uid}]`]));
  }

  onMaterialRemoved(uid: string): void {
    const form = this.form();
    if (form !== null) {
      this.update({ materials: form.materials.filter((material) => material.uid !== uid) });
    }
  }

  private announce(message: string): void {
    this.announcement.set(message);
  }

  private focus(selector: string): void {
    afterNextRender(() => this.host.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }

  // Save and create (BR-16, BR-18, BR-20)

  /** One request at a time; local checks first (BR-08). */
  private prepare(): { readonly body: WorkOrderBody; readonly updatedAt: string | null } | null {
    const form = this.form();
    const editor = this.editor();
    if (form === null || editor === null || this.busy()) {
      return null;
    }
    const errors = formErrors(form, this.todayForBranch());
    this.errors.set(errors);
    this.banner.set(null);
    if (Object.keys(errors).length > 0) {
      this.focusFirstError();
      return null;
    }
    return { body: toBody(form), updatedAt: editor.workOrder?.updatedAt ?? null };
  }

  saveDraft(): void {
    const request = this.prepare();
    if (request === null) {
      return;
    }
    this.submitting.set('draft');
    this.workOrders
      .saveDraft(this.quoteId, request.body, request.updatedAt)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.submitting.set(null);
          const saved = response.body;
          if (saved !== null) {
            this.editor.set(saved);
            this.baseline.set(JSON.stringify(request.body));
          }
        },
        error: (error: unknown) => this.mutationFailed(isApiError(error) ? error : null),
      });
  }

  create(): void {
    const request = this.prepare();
    if (request === null) {
      return;
    }
    this.submitting.set('create');
    this.workOrders
      .create(this.quoteId, request.body, request.updatedAt)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) =>
          this.leaveTo(['/jobs', created.id], {
            severity: 'success',
            summary: `Work order ${created.displayNumber} created.`,
          }),
        error: (error: unknown) => this.mutationFailed(isApiError(error) ? error : null),
      });
  }

  cancel(): void {
    if (!this.busy()) {
      void this.router.navigate(['/quotes', this.quoteId]);
    }
  }

  private mutationFailed(error: ApiError | null): void {
    this.submitting.set(null);
    switch (error?.kind) {
      case 'unauthorized':
        this.onUnauthorized();
        return;
      case 'forbidden':
        this.settle('forbidden');
        return;
      case 'not-found':
        this.settle('not-available');
        return;
      case 'conflict':
        if (this.conflict(error)) {
          return;
        }
        break;
      case 'validation':
      case 'bad-request':
        if (this.applyFieldErrors(error.fieldErrors)) {
          this.focusFirstError();
          return;
        }
        break;
    }
    this.messageService.add({ severity: 'error', summary: WO_SAVE_FAILED_MESSAGE });
  }

  /** `409` per the states table; false when the code is not recognised. */
  private conflict(error: ApiError): boolean {
    switch (error.code) {
      case 'work_order_changed':
        this.banner.set({ message: WO_CHANGED_MESSAGE, reload: true });
        return true;
      case 'request_changed':
        this.banner.set({ message: WO_REQUEST_CHANGED_MESSAGE, reload: true });
        return true;
      case 'work_order_created': {
        const id = this.editor()?.workOrder?.id;
        if (id === undefined) {
          this.loads.next();
        } else {
          this.leaveTo(['/jobs', id], { severity: 'success', summary: WO_ALREADY_CREATED_MESSAGE });
        }
        return true;
      }
      case 'quote_not_approved':
        this.settle('not-approved');
        return true;
      case 'customer_required':
        this.settle('customer-required');
        return true;
      default:
        return false;
    }
  }

  /** Maps server field paths to controls; false when none is recognised. */
  private applyFieldErrors(fieldErrors: Readonly<Record<string, readonly string[]>>): boolean {
    const form = this.form();
    const mapped: Record<string, string> = {};
    for (const [path, messages] of Object.entries(fieldErrors)) {
      const key = form === null ? null : errorKey(path, form);
      if (key !== null && mapped[key] === undefined) {
        mapped[key] = messages[0];
      }
    }
    if (Object.keys(mapped).length === 0) {
      return false;
    }
    this.errors.set(mapped);
    return true;
  }

  /** First invalid control in DOM order, once the errors are rendered. */
  private focusFirstError(): void {
    afterNextRender(
      () => {
        const errors = this.errors();
        const target = Array.from(this.host.querySelectorAll<HTMLElement>('[data-field-key]')).find(
          (element) => (element.dataset['fieldKey'] ?? '') in errors,
        );
        const focusable = target?.matches('input, textarea, button, select')
          ? target
          : target?.querySelector<HTMLElement>('input, textarea, button, select, [tabindex="0"]');
        focusable?.focus();
      },
      { injector: this.injector },
    );
  }

  /** Reload action of the `409` banners: asks first when edits would be lost. */
  reload(): void {
    if (!this.dirty()) {
      this.loads.next();
      return;
    }
    this.confirmationService.confirm(
      discardChangesConfirmation({
        subject: 'this work order',
        accept: () => this.loads.next(),
      }),
    );
  }

  // Navigation

  private leaveTo(commands: readonly string[], toast: ToastHandoff): void {
    this.leaveConfirmed = true;
    void this.router.navigate(commands, { state: { [TOAST_STATE_KEY]: toast } }).finally(() => {
      this.leaveConfirmed = false;
      this.submitting.set(null);
    });
  }

  /** Consulted by `workOrderEditorUnsavedChangesGuard` on route leave (AC-23). */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired() || this.leaveConfirmed || !this.dirty()) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this work order',
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
