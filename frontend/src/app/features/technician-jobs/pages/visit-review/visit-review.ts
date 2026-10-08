import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { SessionService } from '../../../../core/services/session.service';
import { TOAST_STATE_KEY, ToastHandoff } from '../../../requests/models/requests.model';
import { SignaturePad, SignatureStroke } from '../../components/signature-pad/signature-pad';
import { TechnicianVisitDetail } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import {
  AckErrors,
  AckField,
  AckMethod,
  AckValues,
  FIELD_ORDER,
  FIELD_RULES,
  METHODS,
  RELATIONSHIPS,
  ReviewSection,
  SIGNATURE_AGAIN_MESSAGE,
  buildCompletionBody,
  classifyCompleteFailure,
  materialLines,
  serverFieldMessage,
  validateAck,
} from '../../utils/job-completion';
import { formatLabor, laborSeconds } from '../../utils/job-progress';
import {
  NOT_PRIMARY_JOB_MESSAGE,
  TechnicianLoadFailure,
  accessMessage,
  classifyFailure,
} from '../../utils/technician-errors';
import { addressLine } from '../../utils/technician-format';

export const REVIEW_SUBTITLE = 'Review completed work before capturing customer acknowledgment.';
export const ACK_SUBTITLE = 'Choose how completion was acknowledged.';
export const JOB_COMPLETED_MESSAGE = 'Job completed.';
export const NOT_AVAILABLE_MESSAGE = "This job isn't available.";
export const LOAD_ERROR_MESSAGE = "We couldn't load this job. Try again.";
export const COMPLETES_ORDER_TEXT =
  'Completing stops the timer and changes the work order to Completed.';
export const KEEPS_ORDER_TEXT =
  'Completing stops the timer. The work order stays in progress until its remaining visits are completed.';
const TICK_MS = 30_000;

type PageState = 'loading' | 'ready' | 'error' | TechnicianLoadFailure;
type Step = 'review' | 'acknowledgment';
type ItemState = 'met' | 'unmet' | 'neutral';

/**
 * Review & complete (`/today/visits/:visitId/review`, Design 10): the Review step and the Customer
 * acknowledgment step. Entered values live only in this page until Complete job; the backend decides
 * who may complete (frontend hiding is UX only).
 */
@Component({
  selector: 'app-visit-review',
  imports: [RouterLink, ButtonDirective, InputText, Message, Skeleton, SignaturePad],
  templateUrl: './visit-review.html',
  styleUrls: ['./visit-review.scss', './visit-review-ack.scss'],
})
export class VisitReview {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly visitId = inject(ActivatedRoute).snapshot.paramMap.get('visitId') ?? '';
  private readonly title = viewChild<ElementRef<HTMLElement>>('title');
  private readonly pad = viewChild(SignaturePad);
  private prefilled = false;

  readonly jobLink = ['/today/visits', this.visitId];
  readonly todayPath = '/today';
  readonly notAvailableMessage = NOT_AVAILABLE_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly notPrimaryText = NOT_PRIMARY_JOB_MESSAGE;
  readonly methods = METHODS;
  readonly relationships = RELATIONSHIPS;
  readonly fieldRules = FIELD_RULES;

  readonly state = signal<PageState>('loading');
  readonly visit = signal<TechnicianVisitDetail | null>(null);
  readonly step = signal<Step>('review');
  readonly now = signal(Date.now());
  readonly announcement = signal('');
  /** Conflict, other failure or readiness message of the last attempt. */
  readonly formError = signal<string | null>(null);
  readonly pending = signal(false);
  /** A `403 not_primary_technician` on submit: read-only even before the reload answers. */
  readonly lostPrimary = signal(false);

  readonly method = signal<AckMethod>('signed');
  readonly signerName = signal('');
  readonly relationship = signal('customer');
  readonly comment = signal('');
  readonly reviewConfirmed = signal(false);
  readonly strokes = signal<readonly SignatureStroke[]>([]);
  readonly submitted = signal(false);
  private readonly serverErrors = signal<AckErrors>({});

  readonly accessText = computed(() => {
    const state = this.state();
    return state === 'loading' || state === 'ready' || state === 'error'
      ? null
      : accessMessage(state);
  });
  readonly readOnly = computed(() => this.visit()?.isPrimary === false || this.lostPrimary());
  /** The acknowledgment step is never shown to a non-primary technician (BR-17). */
  readonly currentStep = computed<Step>(() => (this.readOnly() ? 'review' : this.step()));
  readonly rules = computed(() => FIELD_RULES[this.method()]);
  readonly values = computed<AckValues>(() => ({
    method: this.method(),
    signerName: this.signerName(),
    relationship: this.relationship(),
    comment: this.comment(),
    reviewConfirmed: this.reviewConfirmed(),
    hasSignature: this.strokes().length > 0,
  }));
  /** Messages shown once Complete job was tried; a server rejection stays until its field changes. */
  readonly errors = computed<AckErrors>(() =>
    this.submitted() ? { ...validateAck(this.values()), ...this.serverErrors() } : {},
  );

  readonly address = computed(() => {
    const visit = this.visit();
    return visit === null ? '' : addressLine(visit.address);
  });
  readonly laborText = computed(() => {
    const visit = this.visit();
    return visit === null ? '' : formatLabor(laborSeconds(visit.time, this.now()));
  });
  readonly tasksDone = computed(
    () => this.visit()?.tasks.filter((task) => task.isCompleted).length ?? 0,
  );
  readonly tasksTotal = computed(() => this.visit()?.tasks.length ?? 0);
  readonly materials = computed(() => {
    const visit = this.visit();
    return visit === null ? { lines: [], more: 0, total: 0 } : materialLines(visit);
  });
  readonly beforeCount = computed(
    () => this.visit()?.evidence.filter((item) => item.type === 'before').length ?? 0,
  );
  readonly afterCount = computed(
    () => this.visit()?.evidence.filter((item) => item.type === 'after').length ?? 0,
  );
  readonly note = computed(() => this.visit()?.technicianNotes?.trim() ?? '');
  readonly ready = computed(() => this.visit()?.completion.ready === true);
  readonly readiness = computed<{ label: string; state: ItemState }[]>(() => {
    const completion = this.visit()?.completion;
    if (completion === undefined) {
      return [];
    }
    return [
      {
        label: completion.requiredTasksComplete
          ? 'Checklist complete'
          : 'Required tasks incomplete',
        state: completion.requiredTasksComplete ? 'met' : 'unmet',
      },
      {
        label:
          completion.hasBeforePhoto && completion.hasAfterPhoto
            ? 'Required evidence attached'
            : 'Before and after photos missing',
        state: completion.hasBeforePhoto && completion.hasAfterPhoto ? 'met' : 'unmet',
      },
      this.materials().total > 0
        ? { label: 'Materials recorded', state: 'met' }
        : { label: 'No materials recorded', state: 'neutral' },
    ];
  });
  readonly footerText = computed(() =>
    this.visit()?.completion.completesWorkOrder ? COMPLETES_ORDER_TEXT : KEEPS_ORDER_TEXT,
  );

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), TICK_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
    // The top bar shows the back action, work order number, status chip and labor time (BR-18).
    effect(() => {
      const visit = this.visit();
      if (this.state() !== 'ready' || visit === null) {
        this.visits.setShellHeader(null);
        return;
      }
      const acknowledgment = this.currentStep() === 'acknowledgment';
      this.visits.setShellHeader({
        backLabel: acknowledgment ? 'Back to review' : 'Back to job',
        onBack: () =>
          acknowledgment ? this.show('review') : void this.router.navigate(this.jobLink),
        title: `#${visit.displayNumber}`,
        status: visit.status === 'paused' ? 'Paused' : 'In progress',
        paused: visit.status === 'paused',
        time: this.laborText(),
      });
    });
    this.destroyRef.onDestroy(() => this.visits.setShellHeader(null));
    // Review & complete is the job's last step, so the bottom-nav progress line shows full.
    effect(() => this.visits.setJobProgress(this.state() === 'ready' ? 100 : null));
    this.destroyRef.onDestroy(() => this.visits.setJobProgress(null));
    if (this.sessionService.session()?.role.code !== 'technician') {
      this.state.set('forbidden');
      return;
    }
    this.load();
  }

  /** Initial load and Retry show the skeleton; a reload after a conflict keeps the page. */
  load(silent = false): void {
    if (!silent) {
      this.state.set('loading');
    }
    this.visits.visit(this.visitId).subscribe({
      next: (visit) => {
        if (visit.status !== 'in_progress' && visit.status !== 'paused') {
          void this.router.navigate(this.jobLink, { replaceUrl: true });
          return;
        }
        if (!this.prefilled) {
          this.prefilled = true;
          this.signerName.set(visit.customerName);
        }
        this.visit.set(visit);
        this.state.set('ready');
      },
      error: (error: unknown) => {
        if (silent) {
          return;
        }
        const failure = classifyFailure(error);
        this.state.set(failure === 'failed' ? 'error' : failure);
      },
    });
  }

  /** Moves between steps; focus lands on the step title (BR-19). */
  show(step: Step): void {
    if (this.pending() || (step === 'acknowledgment' && (this.readOnly() || !this.ready()))) {
      return;
    }
    this.formError.set(null);
    this.step.set(step);
    afterNextRender(() => this.title()?.nativeElement.focus(), { injector: this.injector });
  }

  sectionLink(section: ReviewSection): string {
    return `job-section-${section}`;
  }

  /** Keeps the values of fields the new method also uses and drops the others. */
  selectMethod(method: AckMethod): void {
    const rules = FIELD_RULES[method];
    this.method.set(method);
    if (rules.signerName === null) {
      this.signerName.set(this.visit()?.customerName ?? '');
    }
    if (!rules.relationship) {
      this.relationship.set('customer');
    }
    if (!rules.signature) {
      this.strokes.set([]);
    }
    if (!rules.reviewConfirmed) {
      this.reviewConfirmed.set(false);
    }
    this.serverErrors.set({});
  }

  setField(field: 'signerName' | 'relationship' | 'comment', value: string): void {
    this[field].set(value);
    this.clearServerError(field);
  }

  setConfirmed(value: boolean): void {
    this.reviewConfirmed.set(value);
    this.clearServerError('reviewConfirmed');
  }

  setStrokes(strokes: readonly SignatureStroke[]): void {
    this.strokes.set(strokes);
    this.clearServerError('signature');
  }

  errorId(field: AckField): string {
    return `ack-error-${field}`;
  }

  /** BR-16: validates on the client, then sends only the fields the method allows. */
  async complete(event?: Event): Promise<void> {
    event?.preventDefault();
    if (this.pending() || this.readOnly() || this.currentStep() !== 'acknowledgment') {
      return;
    }
    this.formError.set(null);
    this.serverErrors.set({});
    this.submitted.set(true);
    const first = FIELD_ORDER.find((field) => validateAck(this.values())[field] !== undefined);
    if (first !== undefined) {
      this.focusField(first);
      return;
    }
    this.pending.set(true);
    let signature: Blob | null = null;
    if (this.rules().signature) {
      signature = (await this.pad()?.exportPng()) ?? null;
      if (signature === null) {
        this.pending.set(false);
        this.serverErrors.set({ signature: SIGNATURE_AGAIN_MESSAGE });
        this.focusField('signature');
        return;
      }
    }
    this.visits.complete(this.visitId, buildCompletionBody(this.values(), signature)).subscribe({
      next: () => this.finish(),
      error: (error: unknown) => this.fail(error),
    });
  }

  private finish(): void {
    this.announcement.set(JOB_COMPLETED_MESSAGE);
    const toast: ToastHandoff = { severity: 'success', summary: JOB_COMPLETED_MESSAGE };
    void this.router
      .navigate([this.todayPath], { state: { [TOAST_STATE_KEY]: toast } })
      .finally(() => this.pending.set(false));
  }

  private fail(error: unknown): void {
    const failure = classifyCompleteFailure(error);
    this.pending.set(false);
    switch (failure.kind) {
      case 'not-primary':
        this.lostPrimary.set(true);
        this.step.set('review');
        this.load(true);
        break;
      case 'conflict':
        this.formError.set(failure.message);
        this.step.set('review');
        afterNextRender(() => this.title()?.nativeElement.focus(), { injector: this.injector });
        this.load(true);
        break;
      case 'fields': {
        const values = this.values();
        this.serverErrors.set(
          Object.fromEntries(
            failure.fields.map((field) => [field, serverFieldMessage(field, values)]),
          ),
        );
        this.focusField(
          FIELD_ORDER.find((field) => failure.fields.includes(field)) ?? 'signerName',
        );
        break;
      }
      default:
        this.formError.set(failure.message);
    }
  }

  private clearServerError(field: AckField): void {
    if (this.serverErrors()[field] !== undefined) {
      this.serverErrors.update((errors) => {
        const rest = { ...errors };
        delete rest[field];
        return rest;
      });
    }
  }

  private focusField(field: AckField): void {
    afterNextRender(
      () => {
        if (field === 'signature') {
          this.pad()?.focus();
          return;
        }
        this.host.nativeElement.querySelector<HTMLElement>(`#ack-${field}`)?.focus();
      },
      { injector: this.injector },
    );
  }
}
