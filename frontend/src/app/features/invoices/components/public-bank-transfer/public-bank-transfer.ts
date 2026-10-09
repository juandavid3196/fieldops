import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';

import { isApiError } from '../../../../core/models/api-error.model';
import { BANK_NOTICE_FAILED_MESSAGE, PublicInvoice } from '../../models/invoice.model';
import { PublicInvoiceService } from '../../services/public-invoice.service';
import { formatCalendarDate, money } from '../../utils/public-invoice-format';

/** Bank transfer instructions and the "I sent the transfer" notice (BR-28, BR-29). */
@Component({
  selector: 'app-public-bank-transfer',
  imports: [ButtonDirective, SpinnerIcon],
  templateUrl: './public-bank-transfer.html',
  styleUrl: './public-bank-transfer.scss',
})
export class PublicBankTransfer {
  private readonly service = inject(PublicInvoiceService);
  private readonly destroyRef = inject(DestroyRef);
  /** Own idempotency key: a retry after a failure is the same notice. */
  private readonly key = crypto.randomUUID();

  readonly invoice = input.required<PublicInvoice>();
  /** The invoice is no longer payable: the page re-reads it. */
  readonly reload = output<void>();
  readonly unavailable = output<void>();

  protected readonly sending = signal(false);
  protected readonly failed = signal(false);
  private readonly reportedNow = signal<string | null>(null);
  protected readonly copied = signal<string | null>(null);
  protected readonly failureMessage = BANK_NOTICE_FAILED_MESSAGE;

  protected readonly reportedText = computed(() => {
    const date = this.reportedNow() ?? this.invoice().transferReportedOn;
    return date === null ? null : formatCalendarDate(date);
  });

  protected readonly rows = computed(() => {
    const invoice = this.invoice();
    const bank = invoice.paymentOptions?.bankTransfer;
    const amount = bank?.amount ?? invoice.balanceDue;
    return [
      { label: 'Bank name', value: bank?.bankName ?? '', copy: bank?.bankName ?? '' },
      {
        label: 'Account number',
        value: bank?.accountNumber ?? '',
        copy: bank?.accountNumber ?? '',
      },
      {
        label: 'Routing number',
        value: bank?.routingNumber ?? '',
        copy: bank?.routingNumber ?? '',
      },
      {
        label: 'Payment reference',
        value: bank?.reference ?? invoice.number,
        copy: bank?.reference ?? invoice.number,
      },
      { label: 'Amount', value: money(amount, invoice.currency), copy: amount.toFixed(2) },
    ];
  });

  protected copy(label: string, value: string): void {
    void navigator.clipboard?.writeText(value);
    this.copied.set(label);
    setTimeout(() => this.copied.set(null), 2000);
  }

  protected report(): void {
    if (this.sending()) {
      return;
    }
    this.sending.set(true);
    this.failed.set(false);
    this.service
      .bankTransferNotice(this.key)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.sending.set(false);
          this.reportedNow.set(result.reportedOn);
        },
        error: (error: unknown) => {
          this.sending.set(false);
          const apiError = isApiError(error) ? error : null;
          if (apiError?.status === 404) {
            this.unavailable.emit();
          } else if (apiError?.status === 409 && apiError.code === 'invoice_not_payable') {
            this.reload.emit();
          } else {
            this.failed.set(true);
          }
        },
      });
  }
}
