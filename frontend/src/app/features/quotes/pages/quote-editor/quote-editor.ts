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
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Textarea } from 'primeng/textarea';
import { Toast } from 'primeng/toast';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  debounceTime,
  filter,
  interval,
  map,
  merge,
  of,
  switchMap,
  tap,
} from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import {
  MANAGER_ROLES,
  TOAST_STATE_KEY,
  ToastHandoff,
} from '../../../requests/models/requests.model';
import { addDays, todayInTimeZone } from '../../../service-request/service-request.validators';
import {
  LineAdd,
  LineChange,
  LineMove,
  QuoteLineItems,
} from '../../components/quote-line-items/quote-line-items';
import { QuoteAssessmentCard } from '../../components/quote-assessment-card/quote-assessment-card';
import { QuoteCustomerCard } from '../../components/quote-customer-card/quote-customer-card';
import { QuoteDeliveryCard } from '../../components/quote-delivery-card/quote-delivery-card';
import { QuotePreviewDialog } from '../../components/quote-preview-dialog/quote-preview-dialog';
import { CalcStatus, QuoteSummary } from '../../components/quote-summary/quote-summary';
import {
  CalculatedLine,
  Calculation,
  DraftBody,
  EDITOR_FORBIDDEN_MESSAGE,
  EMAIL_FAILED_MESSAGE,
  NO_RECIPIENT_MESSAGE,
  QUOTE_CHANGED_MESSAGE,
  QUOTE_ID_PATTERN,
  QUOTE_LOAD_ERROR_MESSAGE,
  QUOTE_SAVE_FAILED_MESSAGE,
  QUOTE_UNAVAILABLE_MESSAGE,
  QuoteDetail,
  TERMS_PRESETS,
  TermsPreset,
} from '../../models/quote.model';
import { QuotesService } from '../../services/quotes.service';
import { defaultEmailMessage, previewView, relativeTime } from '../../utils/quote-format';
import {
  DraftState,
  MESSAGE_LIMIT,
  MAX_LINES,
  QuoteLine,
  TERMS_LIMIT,
  addLine,
  customLine,
  draftErrors,
  emailMessageError,
  lineFromCatalog,
  lineName,
  moveLine,
  moveWithinSection,
  stateFromDraft,
  toDraftBody,
} from '../../utils/quote-lines';

export const CALC_DEBOUNCE_MS = 400;
export const NO_NON_OPTIONAL_MESSAGE = "Add at least one line that isn't optional.";
const LINES_LIMIT_MESSAGE = `Add up to ${MAX_LINES} lines.`;
const KNOWN_KEYS: readonly string[] = [
  'discountTotal',
  'customerMessage',
  'internalNote',
  'terms.customText',
  'validUntil',
  'emailMessage',
  'recipient',
  'lines',
];
const LINE_KEY = /^lines\[(\d+)\]\.(\w+)$/i;

type PageState = 'loading' | 'ready' | 'forbidden' | 'not-available' | 'error';

interface CalcState {
  readonly status: CalcStatus;
  readonly data: Calculation | null;
  /** Line `uid`s in the order of the request that produced `data` (matched by index). */
  readonly uids: readonly string[];
}

type CalcResult =
  | { readonly ok: true; readonly data: Calculation; readonly key: string; readonly uids: string[] }
  | { readonly ok: false; readonly error: ApiError | null };

function lowerFirst(value: string): string {
  return value.charAt(0).toLowerCase() + value.slice(1);
}

/**
 * Quote editor (`/quotes/:quoteId/edit`, Design 3). Single owner of the draft: lines, texts, terms,
 * expiration, e-mail message, validation, calculation and submissions. Managers only (checked
 * before any call). The frontend shows only backend-calculated amounts (BR-23).
 */
@Component({
  selector: 'app-quote-editor',
  imports: [
    FormsModule,
    RouterLink,
    ButtonDirective,
    InputText,
    Menu,
    Message,
    Select,
    Skeleton,
    SpinnerIcon,
    Textarea,
    Toast,
    ConfirmDialog,
    DiscardChangesDialog,
    QuoteAssessmentCard,
    QuoteCustomerCard,
    QuoteDeliveryCard,
    QuoteLineItems,
    QuotePreviewDialog,
    QuoteSummary,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './quote-editor.html',
  styleUrl: './quote-editor.scss',
})
export class QuoteEditor {
  private readonly quotes = inject(QuotesService);
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
  private readonly moreMenu = viewChild<Menu>('moreMenu');

  readonly forbiddenMessage = EDITOR_FORBIDDEN_MESSAGE;
  readonly unavailableMessage = QUOTE_UNAVAILABLE_MESSAGE;
  readonly loadErrorMessage = QUOTE_LOAD_ERROR_MESSAGE;
  readonly noRecipientMessage = NO_RECIPIENT_MESSAGE;
  readonly termsPresets = TERMS_PRESETS;
  readonly messageLimit = MESSAGE_LIMIT;
  readonly termsLimit = TERMS_LIMIT;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  /** UX only: the backend decides (BR-07). */
  readonly canManage = computed(() => MANAGER_ROLES.includes(this.roleCode()));
  readonly sessionExpired = signal(false);

  readonly state = signal<PageState>('loading');
  readonly detail = signal<QuoteDetail | null>(null);

  // Draft
  readonly lines = signal<readonly QuoteLine[]>([]);
  readonly discountTotal = signal<number | null>(0);
  readonly customerMessage = signal('');
  readonly internalNote = signal('');
  readonly terms = signal<{ preset: TermsPreset; customText: string }>({
    preset: 'due_on_completion',
    customText: '',
  });
  readonly validUntil = signal('');
  readonly emailMessage = signal('');
  readonly errors = signal<Readonly<Record<string, string>>>({});
  readonly submitting = signal(false);
  readonly announcement = signal('');
  private readonly clock = signal(new Date());

  private readonly draftState = computed<DraftState>(() => ({
    lines: this.lines(),
    discountTotal: this.discountTotal(),
    customerMessage: this.customerMessage(),
    internalNote: this.internalNote(),
    terms: this.terms(),
    validUntil: this.validUntil(),
  }));
  /** The request body of the current form (`DraftBody`); null until a draft is loaded. */
  readonly draft = computed<DraftBody | null>(() =>
    this.detail()?.draft ? toDraftBody(this.draftState()) : null,
  );
  private readonly baseline = signal('');
  readonly dirty = computed(() => {
    const draft = this.draft();
    return draft !== null && JSON.stringify(draft) !== this.baseline();
  });
  private readonly localErrors = computed(() => draftErrors(this.draftState()));
  readonly invalid = computed(() => Object.keys(this.localErrors()).length > 0);

  // Calculation
  readonly calc = signal<CalcState>({ status: 'idle', data: null, uids: [] });
  private calcKey = '';
  private readonly calcRetry = new Subject<void>();
  readonly summaryInvalid = computed(() => this.invalid() || this.calc().status === 'idle');
  readonly amounts = computed(() => {
    const { data, uids } = this.calc();
    const map = new Map<string, CalculatedLine>();
    uids.forEach((uid, index) => {
      const line = data?.lines[index];
      if (line !== undefined) {
        map.set(uid, line);
      }
    });
    return map as ReadonlyMap<string, CalculatedLine>;
  });

  // Preview
  readonly previewOpen = signal(false);
  readonly previewModel = computed(() => {
    const detail = this.detail();
    const draft = this.draft();
    const { status, data } = this.calc();
    if (
      detail === null ||
      draft === null ||
      data === null ||
      status !== 'ready' ||
      this.invalid()
    ) {
      return null;
    }
    return previewView(detail, draft, data, detail.draft?.versionNo ?? 1, this.clock());
  });

  // Header
  readonly title = computed(() =>
    (this.detail()?.draft?.versionNo ?? 1) > 1 ? 'Revise quote' : 'Create quote',
  );
  readonly saveState = computed(() => {
    const detail = this.detail();
    if (detail === null) {
      return { label: '', time: '' };
    }
    return this.dirty()
      ? { label: 'Unsaved changes', time: '' }
      : { label: 'Draft saved', time: relativeTime(detail.updatedAt, this.clock(), 'UTC') };
  });
  readonly minDate = computed(() => addDays(todayInTimeZone(undefined, this.clock()), 1));
  readonly maxDate = computed(() => addDays(todayInTimeZone(undefined, this.clock()), 365));
  readonly requestQueryParams = computed(() => ({ request: this.detail()?.request.id ?? '' }));
  readonly moreItems = computed<MenuItem[]>(() => [
    { label: 'Discard draft', disabled: this.submitting(), command: () => this.confirmDiscard() },
  ]);
  readonly sendBlocked = computed(() => (this.detail()?.recipient.email ?? null) === null);

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

    // BR-23: recalculate after each valid change, debounced; a call is skipped for an invalid form.
    merge(
      toObservable(this.draft).pipe(
        filter((draft): draft is DraftBody => draft !== null),
        tap((draft) => this.markPending(draft)),
        debounceTime(CALC_DEBOUNCE_MS),
      ),
      this.calcRetry.pipe(
        map(() => this.draft()),
        filter((draft): draft is DraftBody => draft !== null),
      ),
    )
      .pipe(
        switchMap((draft) => this.calculate$(draft)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleCalc(result));

    interval(30_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.clock.set(new Date()));

    // The role is checked before any call (BR-07); an invalid id never reaches the API.
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
    | { readonly kind: 'ready'; readonly detail: QuoteDetail }
    | { readonly kind: 'state'; readonly state: PageState }
    | { readonly kind: 'unauthorized' }
  > {
    this.state.set('loading');
    return this.quotes.get(this.quoteId).pipe(
      map((detail) => ({ kind: 'ready' as const, detail })),
      catchError((error: unknown) => {
        switch (isApiError(error) ? error.kind : null) {
          case 'unauthorized':
            return of({ kind: 'unauthorized' as const });
          case 'forbidden':
            return of({ kind: 'state' as const, state: 'forbidden' as const });
          case 'not-found':
            return of({ kind: 'state' as const, state: 'not-available' as const });
          default:
            return of({ kind: 'state' as const, state: 'error' as const });
        }
      }),
    );
  }

  retryLoad(): void {
    this.loads.next();
  }

  private handleLoad(
    result:
      | { readonly kind: 'ready'; readonly detail: QuoteDetail }
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
    const { detail } = result;
    if (!detail.canManage) {
      this.settle('forbidden');
      return;
    }
    if (detail.status === 'cancelled') {
      this.settle('not-available');
      return;
    }
    if (detail.draft === null) {
      this.goToView();
      return;
    }
    this.applyDetail(detail, true);
    this.settle('ready');
  }

  /** Replaces the form with a server detail (load, save, refresh) and resets baseline and calculation. */
  private applyDetail(detail: QuoteDetail, resetMessage: boolean): void {
    const draft = detail.draft;
    if (draft === null) {
      return;
    }
    const next = stateFromDraft(draft, () => crypto.randomUUID());
    this.detail.set(detail);
    this.lines.set(next.lines);
    this.discountTotal.set(next.discountTotal);
    this.customerMessage.set(next.customerMessage);
    this.internalNote.set(next.internalNote);
    this.terms.set({ ...next.terms });
    this.validUntil.set(next.validUntil);
    this.errors.set({});
    const key = JSON.stringify(toDraftBody(next));
    this.baseline.set(key);
    this.calcKey = key;
    this.calc.set({
      status: 'ready',
      data: draft.calculation,
      uids: next.lines.map((line) => line.uid),
    });
    if (resetMessage) {
      this.emailMessage.set(defaultEmailMessage(detail.customer.name, detail.request.title));
    }
  }

  private settle(state: PageState): void {
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  // Calculation pipeline

  private markPending(draft: DraftBody): void {
    if (this.invalid()) {
      this.calc.update((calc) => ({ ...calc, status: 'idle' }));
    } else {
      const ready = JSON.stringify(draft) === this.calcKey;
      this.calc.update((calc) => ({ ...calc, status: ready ? 'ready' : 'loading' }));
    }
  }

  private calculate$(draft: DraftBody): Observable<CalcResult> {
    const key = JSON.stringify(draft);
    if (this.invalid() || key === this.calcKey) {
      return EMPTY;
    }
    const uids = this.lines().map((line) => line.uid);
    this.calc.update((calc) => ({ ...calc, status: 'loading' }));
    return this.quotes.calculate(this.quoteId, draft).pipe(
      map((data): CalcResult => ({ ok: true, data, key, uids })),
      catchError((error: unknown) =>
        of<CalcResult>({ ok: false, error: isApiError(error) ? error : null }),
      ),
    );
  }

  private handleCalc(result: CalcResult): void {
    if (result.ok) {
      this.calcKey = result.key;
      const current = this.draft();
      this.calc.set({
        status: current !== null && JSON.stringify(current) === result.key ? 'ready' : 'loading',
        data: result.data,
        uids: result.uids,
      });
      return;
    }
    const error = result.error;
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
        if (error.code === 'no_draft') {
          this.goToView();
          return;
        }
        break;
      case 'validation':
        // The form is invalid for the server (e.g. discount above the subtotal): no values shown.
        if (this.applyFieldErrors(error.fieldErrors)) {
          this.calc.update((calc) => ({ ...calc, status: 'idle' }));
          return;
        }
        break;
    }
    this.calc.update((calc) => ({ ...calc, status: 'error' }));
  }

  retryCalculation(): void {
    this.calc.update((calc) => ({ ...calc, status: 'loading' }));
    this.calcRetry.next();
  }

  // Form changes

  private clearErrors(...keys: string[]): void {
    if (keys.some((key) => key in this.errors())) {
      this.errors.update((errors) =>
        Object.fromEntries(Object.entries(errors).filter(([key]) => !keys.includes(key))),
      );
    }
  }

  onLineChange({ uid, patch }: LineChange): void {
    this.lines.update((lines) =>
      lines.map((line) => (line.uid === uid ? { ...line, ...patch } : line)),
    );
    this.clearErrors(...Object.keys(patch).map((field) => `${uid}.${field}`));
  }

  onDiscount(value: number | null): void {
    this.discountTotal.set(value);
    this.clearErrors('discountTotal');
  }

  onCustomerMessage(value: string): void {
    this.customerMessage.set(value);
    this.clearErrors('customerMessage');
  }

  onInternalNote(value: string): void {
    this.internalNote.set(value);
    this.clearErrors('internalNote');
  }

  onPreset(preset: TermsPreset): void {
    this.terms.update((terms) => ({ ...terms, preset }));
    this.clearErrors('terms.customText');
  }

  onCustomTerms(customText: string): void {
    this.terms.update((terms) => ({ ...terms, customText }));
    this.clearErrors('terms.customText');
  }

  onValidUntil(value: string): void {
    this.validUntil.set(value);
    this.clearErrors('validUntil');
  }

  onEmailMessage(value: string): void {
    this.emailMessage.set(value);
    this.clearErrors('emailMessage');
  }

  // Lines: add, remove, reorder (BR-09, BR-12)

  onAddLine(add: LineAdd): void {
    if (this.lines().length >= MAX_LINES) {
      this.errors.update((errors) => ({ ...errors, lines: LINES_LIMIT_MESSAGE }));
      return;
    }
    const uid = crypto.randomUUID();
    const line =
      add.item === null
        ? customLine(add.type, add.isOptional, uid)
        : lineFromCatalog(add.item, add.type, add.isOptional, uid);
    this.lines.update((lines) => addLine(lines, line));
    this.clearErrors('lines');
    this.announce(`Added ${add.item?.name ?? 'a new line'} to ${this.section(line)} items.`);
    this.focus(`[data-field-key="${uid}.name"]`);
  }

  onRemoveLine(uid: string): void {
    const lines = this.lines();
    const index = lines.findIndex((line) => line.uid === uid);
    if (index < 0) {
      return;
    }
    const removed = lines[index];
    const next = lines.filter((line) => line.uid !== uid);
    this.lines.set(next);
    this.announce(`Removed ${lineName(removed, index)}.`);
    this.focus(
      next[index] !== undefined
        ? `[data-field-key="${next[index].uid}.name"]`
        : '.items__add button',
    );
  }

  onMoveLine({ uid, direction }: LineMove): void {
    const from = this.lines().findIndex((line) => line.uid === uid);
    this.applyMove(moveWithinSection(this.lines(), from, direction));
  }

  onDropLine({ from, to }: { readonly from: number; readonly to: number }): void {
    this.applyMove(moveLine(this.lines(), from, to));
  }

  private applyMove(move: ReturnType<typeof moveLine>): void {
    if (move === null) {
      return;
    }
    this.lines.set(move.lines);
    const index = move.lines.indexOf(move.line);
    this.announce(
      `Moved ${lineName(move.line, index)} to position ${move.position} of ${move.count} in ${this.section(move.line)} items.`,
    );
    this.focus(`[data-menu-uid="${move.line.uid}"]`);
  }

  private section(line: { readonly isOptional: boolean }): string {
    return line.isOptional ? 'Optional' : 'Regular';
  }

  private announce(message: string): void {
    this.announcement.set(message);
  }

  private focus(selector: string): void {
    afterNextRender(() => this.host.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }

  // Save and send (BR-22, BR-24)

  save(): void {
    const detail = this.detail();
    const draft = this.draft();
    if (detail === null || draft === null || this.submitting() || !this.dirty()) {
      return;
    }
    if (!this.validate(false)) {
      return;
    }
    this.submitting.set(true);
    this.quotes
      .saveDraft(this.quoteId, draft, detail.updatedAt)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.submitting.set(false);
          this.applyDetail(saved, false);
          this.messageService.add({ severity: 'success', summary: 'Draft saved.' });
        },
        error: (error: unknown) => this.mutationFailed(isApiError(error) ? error : null),
      });
  }

  /** Local checks before a request; shows the errors and focuses the first invalid control. */
  private validate(forSend: boolean): boolean {
    const errors: Record<string, string> = { ...this.localErrors() };
    if (forSend) {
      const message = emailMessageError(this.emailMessage());
      if (message !== null) {
        errors['emailMessage'] = message;
      }
      if ((this.detail()?.recipient.email ?? null) === null) {
        errors['recipient'] = NO_RECIPIENT_MESSAGE;
      }
      if (this.lines().every((line) => line.isOptional)) {
        errors['lines'] = NO_NON_OPTIONAL_MESSAGE;
      }
    }
    this.errors.set(errors);
    if (Object.keys(errors).length === 0) {
      return true;
    }
    this.focusFirstError();
    return false;
  }

  send(): void {
    const detail = this.detail();
    if (detail === null || this.submitting() || !this.validate(true)) {
      return;
    }
    const versionNo = detail.draft?.versionNo ?? 1;
    this.confirmationService.confirm({
      header: 'Send quote?',
      message: `${detail.customer.name} will receive version ${versionNo} of ${detail.displayNumber} at ${detail.recipient.email}. A sent version can't be edited; later changes create a new version.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Send quote' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.doSend(),
    });
  }

  private doSend(): void {
    const detail = this.detail();
    const draft = this.draft();
    if (detail === null || draft === null || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.quotes
      .send(this.quoteId, draft, detail.updatedAt, this.emailMessage().trim())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) =>
          this.leaveTo(['/quotes', this.quoteId], {
            severity: result.emailStatus === 'failed' ? 'warn' : 'success',
            summary: result.emailStatus === 'failed' ? EMAIL_FAILED_MESSAGE : 'Quote sent.',
          }),
        error: (error: unknown) => this.mutationFailed(isApiError(error) ? error : null),
      });
  }

  // Discard (BR-29)

  openMoreMenu(event: Event): void {
    this.moreMenu()?.toggle(event);
  }

  private confirmDiscard(): void {
    const detail = this.detail();
    if (detail === null || this.submitting()) {
      return;
    }
    const versionNo = detail.draft?.versionNo ?? 1;
    const first = versionNo <= 1;
    this.confirmationService.confirm({
      header: 'Discard draft?',
      message: first
        ? `The quote ${detail.displayNumber} will be cancelled. Its number won't be reused.`
        : `Your changes to version ${versionNo} will be deleted. Version ${versionNo - 1} stays with the customer.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Discard draft', severity: 'danger' },
      rejectButtonProps: { label: 'Keep editing', severity: 'secondary', outlined: true },
      accept: () => this.doDiscard(first),
    });
  }

  private doDiscard(first: boolean): void {
    const detail = this.detail();
    if (detail === null || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.quotes
      .discardDraft(this.quoteId, detail.updatedAt)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          const toast: ToastHandoff = { severity: 'success', summary: 'Draft discarded.' };
          if (first) {
            this.leaveTo(['/requests'], toast, { request: result.requestId });
          } else {
            this.leaveTo(['/quotes', this.quoteId], toast);
          }
        },
        error: (error: unknown) => this.mutationFailed(isApiError(error) ? error : null),
      });
  }

  // Errors

  private mutationFailed(error: ApiError | null): void {
    this.submitting.set(false);
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
        if (error.code === 'no_draft') {
          this.goToView();
          return;
        }
        if (error.code === 'quote_changed') {
          this.messageService.add({
            key: 'quote-changed',
            severity: 'warn',
            summary: QUOTE_CHANGED_MESSAGE,
            sticky: true,
          });
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
    this.messageService.add({ severity: 'error', summary: QUOTE_SAVE_FAILED_MESSAGE });
  }

  /** Maps `lines[i].key` and the top-level keys to controls; false when none is recognised. */
  private applyFieldErrors(fieldErrors: Readonly<Record<string, readonly string[]>>): boolean {
    const mapped: Record<string, string> = {};
    const lines = this.lines();
    for (const [path, messages] of Object.entries(fieldErrors)) {
      const lineMatch = LINE_KEY.exec(path);
      if (lineMatch !== null) {
        const line = lines[Number(lineMatch[1])];
        if (line !== undefined) {
          mapped[`${line.uid}.${lowerFirst(lineMatch[2])}`] = messages[0];
        }
        continue;
      }
      const key = path.split('.').map(lowerFirst).join('.');
      if (KNOWN_KEYS.includes(key)) {
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
          : target?.querySelector<HTMLElement>('input, textarea, button, select');
        focusable?.focus();
      },
      { injector: this.injector },
    );
  }

  /** Refresh action of the `quote_changed` toast: reload, asking first when edits would be lost. */
  refresh(): void {
    this.messageService.clear('quote-changed');
    if (!this.dirty()) {
      this.loads.next();
      return;
    }
    this.confirmationService.confirm(
      discardChangesConfirmation({
        subject: 'this quote',
        accept: () => this.loads.next(),
      }),
    );
  }

  // Navigation

  private goToView(): void {
    this.leaveConfirmed = true;
    void this.router
      .navigate(['/quotes', this.quoteId], { replaceUrl: true })
      .finally(() => (this.leaveConfirmed = false));
  }

  private leaveTo(
    commands: readonly string[],
    toast: ToastHandoff,
    queryParams?: Record<string, string>,
  ): void {
    this.leaveConfirmed = true;
    void this.router
      .navigate(commands, { queryParams, state: { [TOAST_STATE_KEY]: toast } })
      .finally(() => {
        this.leaveConfirmed = false;
        this.submitting.set(false);
      });
  }

  /** Consulted by `quoteEditorUnsavedChangesGuard` on route leave (BR-22). */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired() || this.leaveConfirmed || !this.dirty()) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this quote',
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
