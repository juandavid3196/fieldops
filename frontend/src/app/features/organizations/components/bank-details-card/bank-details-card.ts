import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Skeleton } from 'primeng/skeleton';

import { isApiError } from '../../../../core/models/api-error.model';
import {
  BANK_CHANGED_MESSAGE,
  BANK_FIELD_MESSAGES,
  BANK_HELPER_MESSAGE,
  BANK_LOAD_ERROR_MESSAGE,
  BANK_SAVED_MESSAGE,
  BANK_SAVE_FAILED_MESSAGE,
  BANK_UNAVAILABLE_MESSAGE,
  BankDetails,
  BankField,
  BankFieldErrors,
  validateBankDetails,
} from '../../models/bank-details.model';
import { BankDetailsService } from '../../services/bank-details.service';
import { FormField } from '../form-field/form-field';

const FIELDS: readonly BankField[] = ['bankName', 'accountNumber', 'routingNumber'];

/**
 * "Bank transfer details" card of Company setup (BR-32, Owner only): its own form and Save button,
 * separate from the page's Save changes. The stored account number is only ever shown masked.
 */
@Component({
  selector: 'app-bank-details-card',
  imports: [ReactiveFormsModule, ButtonDirective, FormField, InputText, Skeleton, SpinnerIcon],
  templateUrl: './bank-details-card.html',
  styleUrl: './bank-details-card.scss',
})
export class BankDetailsCard {
  private readonly service = inject(BankDetailsService);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly accountInput = viewChild<ElementRef<HTMLInputElement>>('account');

  readonly unauthorized = output<void>();

  readonly form = new FormGroup({
    bankName: new FormControl('', { nonNullable: true }),
    accountNumber: new FormControl('', { nonNullable: true }),
    routingNumber: new FormControl('', { nonNullable: true }),
  });

  protected readonly helper = BANK_HELPER_MESSAGE;
  protected readonly loadErrorMessage = BANK_LOAD_ERROR_MESSAGE;

  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly details = signal<BankDetails | null>(null);
  protected readonly replacing = signal(true);
  protected readonly saving = signal(false);
  protected readonly errors = signal<BankFieldErrors>({});
  protected readonly formMessage = signal<string | null>(null);
  /** The server rejected the save because the details changed elsewhere. */
  protected readonly stale = signal(false);

  constructor() {
    this.load();
  }

  /** Read by the page's route-leave guard. */
  isDirty(): boolean {
    return this.form.dirty;
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.formMessage.set(null);
    this.stale.set(false);
    this.service
      .get()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (details) => {
          this.loading.set(false);
          this.apply(details);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          if (isApiError(error) && error.kind === 'unauthorized') {
            this.unauthorized.emit();
          } else {
            this.loadError.set(true);
          }
        },
      });
  }

  private apply(details: BankDetails): void {
    this.details.set(details);
    this.replacing.set(!details.configured);
    this.errors.set({});
    this.form.reset({
      bankName: details.bankName ?? '',
      accountNumber: '',
      routingNumber: details.routingNumber ?? '',
    });
  }

  protected error(field: BankField): string | null {
    return this.errors()[field] ?? null;
  }

  protected describedBy(field: BankField): string | null {
    return this.error(field) === null ? null : `bank-${field}-error`;
  }

  protected replace(): void {
    this.replacing.set(true);
    afterNextRender(() => this.accountInput()?.nativeElement.focus(), { injector: this.injector });
  }

  protected save(): void {
    const details = this.details();
    if (this.saving() || details === null) {
      return;
    }
    const values = this.form.getRawValue();
    const replacing = this.replacing();
    const errors = validateBankDetails(
      { ...values, accountNumber: replacing ? values.accountNumber : '' },
      !details.configured,
    );
    this.errors.set(errors);
    this.formMessage.set(null);
    this.stale.set(false);
    const first = FIELDS.find((field) => errors[field] !== undefined);
    if (first !== undefined) {
      document.getElementById(`bank-${first}`)?.focus();
      return;
    }
    this.saving.set(true);
    this.service
      .update({
        bankName: values.bankName.trim(),
        accountNumber: replacing ? values.accountNumber.trim() : '',
        routingNumber: values.routingNumber.trim(),
        updatedAt: details.updatedAt,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.apply(saved);
          this.messages.add({ severity: 'success', summary: BANK_SAVED_MESSAGE });
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.failed(error);
        },
      });
  }

  private failed(error: unknown): void {
    const apiError = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
    } else if (apiError?.kind === 'validation') {
      const mapped: BankFieldErrors = {};
      for (const key of Object.keys(apiError.fieldErrors)) {
        const field = FIELDS.find((name) => key.toLowerCase().endsWith(name.toLowerCase()));
        if (field !== undefined) {
          mapped[field] = BANK_FIELD_MESSAGES[field];
        }
      }
      this.errors.set(mapped);
      this.formMessage.set(Object.keys(mapped).length > 0 ? null : BANK_SAVE_FAILED_MESSAGE);
    } else if (apiError?.status === 409) {
      this.formMessage.set(BANK_CHANGED_MESSAGE);
      this.stale.set(true);
    } else if (apiError?.status === 503) {
      this.formMessage.set(BANK_UNAVAILABLE_MESSAGE);
    } else {
      this.formMessage.set(BANK_SAVE_FAILED_MESSAGE);
    }
  }
}
