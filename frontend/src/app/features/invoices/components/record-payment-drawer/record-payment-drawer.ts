import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputGroup } from 'primeng/inputgroup';
import { InputGroupAddon } from 'primeng/inputgroupaddon';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { Subscription } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { FormField } from '../../../organizations/components/form-field/form-field';
import {
  InvoiceRow,
  InvoicesOptions,
  NOTE_MAX_LENGTH,
  NO_RECIPIENT_MESSAGE,
  PAYMENT_ALREADY_RECORDED_MESSAGE,
  PAYMENT_CONFLICT_MESSAGES,
  PAYMENT_FAILED_MESSAGE,
  PAYMENT_FORBIDDEN_MESSAGE,
  PAYMENT_METHODS,
  PAYMENT_METHOD_LABELS,
  PAYMENT_UNAVAILABLE_MESSAGE,
  PaymentMethod,
  REFERENCE_REQUIRED_METHODS,
  RECEIPT_FAILED_MESSAGE,
  RECEIPT_SENT_MESSAGE,
  RecordPaymentResult,
} from '../../models/invoices-hub.model';
import { InvoicesHubService } from '../../services/invoices-hub.service';
import {
  PaymentErrors,
  PaymentField,
  PaymentValues,
  firstInvalidField,
  money,
  parseAmount,
  todayIn,
  validatePayment,
} from '../../utils/invoices-hub-format';

export interface DrawerNotice {
  readonly severity: 'success' | 'info' | 'warn' | 'error';
  readonly summary: string;
}

const FIELD_KEYS: Readonly<Record<string, PaymentField>> = {
  amount: 'amount',
  paiddate: 'paidDate',
  method: 'method',
  reference: 'reference',
  receivedbyuserid: 'receivedByUserId',
  note: 'note',
};

/** Server field key (`errors.<name>`) to this form's field. */
function fieldOf(path: string): PaymentField | null {
  const last = (path.split('.').pop() ?? path).toLowerCase();
  return FIELD_KEYS[last] ?? null;
}

/**
 * Record external payment drawer (BR-25, BR-26): read-only invoice data, the payment fields,
 * client validation, the idempotent submit and the server outcomes. The page owns closing,
 * the toasts and the reloads behind `notice` and `settled`.
 */
@Component({
  selector: 'app-record-payment-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    DrawerShell,
    FormField,
    InputGroup,
    InputGroupAddon,
    InputText,
    Select,
    SpinnerIcon,
    Textarea,
    ToggleSwitch,
  ],
  templateUrl: './record-payment-drawer.html',
  styleUrl: './record-payment-drawer.scss',
})
export class RecordPaymentDrawer {
  private readonly api = inject(InvoicesHubService);
  private readonly session = inject(SessionService);
  private readonly confirmations = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly open = input.required<boolean>();
  readonly row = input<InvoiceRow | null>(null);
  readonly options = input<InvoicesOptions | null>(null);

  /** The drawer was dismissed by the user. */
  readonly closed = output<void>();
  /** An outcome ended the flow: the page closes the drawer and reloads. */
  readonly settled = output<void>();
  readonly notice = output<DrawerNotice>();
  readonly unauthorized = output<void>();

  readonly titleId = 'record-payment-title';
  readonly noteMax = NOTE_MAX_LENGTH;
  readonly noRecipientMessage = NO_RECIPIENT_MESSAGE;
  readonly methodChoices = PAYMENT_METHODS.map((code) => ({
    code,
    label: PAYMENT_METHOD_LABELS[code],
  }));

  readonly amount = signal('');
  readonly paidDate = signal('');
  readonly method = signal<PaymentMethod | null>(null);
  readonly reference = signal('');
  readonly receivedByUserId = signal('');
  readonly sendReceipt = signal(true);
  readonly note = signal('');
  readonly errors = signal<PaymentErrors>({});
  readonly submitting = signal(false);

  private idempotencyKey = '';
  private snapshot = '';
  private submit$: Subscription | null = null;

  readonly timezone = computed(() => this.options()?.timezone ?? 'UTC');
  readonly today = computed(() => todayIn(this.timezone()));
  readonly currency = computed(() => this.row()?.currency ?? this.options()?.currency ?? 'USD');
  readonly currencySymbol = computed(
    () =>
      new Intl.NumberFormat('en-US', { style: 'currency', currency: this.currency() })
        .formatToParts(0)
        .find((part) => part.type === 'currency')?.value ?? '$',
  );
  readonly balanceText = computed(() => {
    const row = this.row();
    return row === null ? '' : money(row.balanceDue, row.currency);
  });
  readonly memberChoices = computed(() =>
    (this.options()?.members ?? []).map((member) => ({
      code: member.userId,
      label: member.name,
    })),
  );
  readonly hasRecipient = computed(() => this.row()?.hasRecipient ?? false);
  readonly referenceRequired = computed(() => {
    const method = this.method();
    return method !== null && REFERENCE_REQUIRED_METHODS.includes(method);
  });
  readonly dirty = computed(() => this.formKey() !== this.snapshot);

  private readonly formKey = computed(() =>
    JSON.stringify([
      this.amount(),
      this.paidDate(),
      this.method(),
      this.reference(),
      this.receivedByUserId(),
      this.sendReceipt(),
      this.note(),
    ]),
  );

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => this.reset());
      }
    });
    this.destroyRef.onDestroy(() => this.submit$?.unsubscribe());
  }

  /** A new idempotency key and the BR-25 defaults on every open. */
  private reset(): void {
    this.submit$?.unsubscribe();
    const row = this.row();
    this.idempotencyKey = crypto.randomUUID();
    this.submitting.set(false);
    this.errors.set({});
    this.amount.set(row === null ? '' : row.balanceDue.toFixed(2));
    this.paidDate.set(this.today());
    this.method.set(null);
    this.reference.set('');
    const userId = this.session.session()?.user.id ?? '';
    this.receivedByUserId.set(this.memberChoices().some((m) => m.code === userId) ? userId : '');
    this.sendReceipt.set(row?.hasRecipient ?? false);
    this.note.set('');
    this.snapshot = this.formKey();
  }

  error(field: PaymentField): string | null {
    return this.errors()[field] ?? null;
  }

  describedBy(field: PaymentField): string | null {
    return this.error(field) === null ? null : `record-payment-${field}-error`;
  }

  clear(field: PaymentField): void {
    if (this.errors()[field] !== undefined) {
      this.errors.update((current) => {
        const rest = { ...current };
        delete rest[field];
        return rest;
      });
    }
  }

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.dirty()) {
      this.confirmations.confirm(
        discardChangesConfirmation({
          subject: 'this payment',
          accept: () => this.closed.emit(),
        }),
      );
      return;
    }
    this.closed.emit();
  }

  private values(): PaymentValues {
    return {
      amount: this.amount(),
      paidDate: this.paidDate(),
      method: this.method(),
      reference: this.reference(),
      receivedByUserId: this.receivedByUserId(),
      note: this.note(),
    };
  }

  /** Focuses the first invalid field in DOM order (BR-26). */
  private focusFirst(errors: PaymentErrors): void {
    const field = firstInvalidField(errors);
    if (field !== null) {
      document.getElementById(`record-payment-${field}`)?.focus();
    }
  }

  save(): void {
    const row = this.row();
    const method = this.method();
    if (this.submitting() || row === null) {
      return;
    }
    const errors = validatePayment(this.values(), {
      balance: row.balanceDue,
      currency: row.currency,
      issueDate: row.issueDate,
      today: this.today(),
    });
    this.errors.set(errors);
    const amount = parseAmount(this.amount());
    if (method === null || amount === null || Object.keys(errors).length > 0) {
      this.focusFirst(errors);
      return;
    }
    const reference = this.reference().trim();
    const note = this.note().trim();
    this.submitting.set(true);
    this.submit$ = this.api
      .recordPayment(row.id, {
        idempotencyKey: this.idempotencyKey,
        amount,
        paidDate: this.paidDate(),
        method,
        reference: reference === '' ? null : reference,
        receivedByUserId: this.receivedByUserId(),
        sendReceipt: this.hasRecipient() && this.sendReceipt(),
        note: note === '' ? null : note,
        updatedAt: row.updatedAt,
      })
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.succeeded(result);
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          this.failed(error);
        },
      });
  }

  private succeeded(result: RecordPaymentResult): void {
    if (!result.changed) {
      this.notice.emit({ severity: 'info', summary: PAYMENT_ALREADY_RECORDED_MESSAGE });
    } else if (result.emailStatus === 'failed') {
      this.notice.emit({ severity: 'warn', summary: RECEIPT_FAILED_MESSAGE });
    } else {
      const recorded = `Payment ${result.payment.number} recorded.`;
      this.notice.emit({
        severity: 'success',
        summary: result.emailStatus === 'sent' ? `${recorded} ${RECEIPT_SENT_MESSAGE}` : recorded,
      });
    }
    this.settled.emit();
  }

  private failed(error: unknown): void {
    const apiError = isApiError(error) ? error : null;
    const kind = apiError?.kind ?? null;
    if (kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    const code = apiError?.code ?? '';
    if (kind === 'conflict' && code in PAYMENT_CONFLICT_MESSAGES) {
      this.notice.emit({ severity: 'error', summary: PAYMENT_CONFLICT_MESSAGES[code] });
      this.settled.emit();
    } else if (kind === 'not-found') {
      this.notice.emit({ severity: 'error', summary: PAYMENT_UNAVAILABLE_MESSAGE });
      this.settled.emit();
    } else if (kind === 'forbidden') {
      this.notice.emit({ severity: 'error', summary: PAYMENT_FORBIDDEN_MESSAGE });
    } else if (kind === 'validation' || kind === 'bad-request') {
      const mapped: PaymentErrors = {};
      for (const [path, messages] of Object.entries(apiError?.fieldErrors ?? {})) {
        const field = fieldOf(path);
        if (field !== null && messages.length > 0) {
          mapped[field] = messages[0];
        }
      }
      this.errors.set(mapped);
      if (Object.keys(mapped).length > 0) {
        this.focusFirst(mapped);
      } else {
        this.notice.emit({ severity: 'error', summary: PAYMENT_FAILED_MESSAGE });
      }
    } else {
      // `409 idempotency_conflict`, network and server failures: values and key are kept.
      this.notice.emit({ severity: 'error', summary: PAYMENT_FAILED_MESSAGE });
    }
  }
}
