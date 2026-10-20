import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { Observable } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { AdministrationNav } from '../../components/administration-nav/administration-nav';
import {
  BUSINESS_HOURS_DAYS,
  BUSINESS_HOURS_DAY_LABELS,
  BusinessHoursBody,
  BusinessHoursDayName,
  BusinessHoursErrors,
  BusinessHoursRow,
} from '../../models/business-hours.model';
import { BranchListItem } from '../../models/company-settings.model';
import { BranchesService } from '../../services/branches.service';
import { BusinessHoursService } from '../../services/business-hours.service';
import {
  BusinessHoursPart,
  DEFAULT_CLOSING,
  DEFAULT_OPENING,
  TimeOption,
  buildRequest,
  controlId,
  errorKey,
  mapServerErrors,
  rowsFromBody,
  rowsSignature,
  timeOptions,
  timezoneLabel,
  validateRow,
  validateRows,
} from '../../utils/business-hours-form';
import { handleUnauthorized } from '../../utils/handle-unauthorized';

export const DESCRIPTION_MESSAGE = 'Set the standard operating hours for each branch.';
export const FORBIDDEN_MESSAGE = "You don't have access to business hours.";
export const READ_ONLY_MESSAGE = 'You have view-only access to business hours.';
export const BRANCHES_ERROR_MESSAGE = "We couldn't load branches.";
export const HOURS_ERROR_MESSAGE = "We couldn't load business hours.";
export const EMPTY_MESSAGE = 'No branches available.';
export const STALE_MESSAGE =
  'This record was changed by someone else. Reload to see the latest version.';
export const BRANCH_GONE_MESSAGE = 'This branch is no longer available.';
export const INFO_MESSAGE =
  'Business hours define when this branch normally operates. Technician availability is managed separately.';
export const SCHEDULING_NOTE =
  'Visits can still be scheduled outside business hours, but Dispatch will see a warning.';

const PARTS: readonly BusinessHoursPart[] = ['openingTime', 'closingTime'];

/**
 * Business hours page (`/admin/business-hours`): branch selector, read-only time zone and the
 * Monday-Sunday editor. Toast/confirm providers are page-level, like Company setup.
 */
@Component({
  selector: 'app-business-hours',
  imports: [
    DiscardChangesDialog,
    FormsModule,
    ReactiveFormsModule,
    RouterLink,
    ButtonDirective,
    InputText,
    Message,
    Select,
    Skeleton,
    SpinnerIcon,
    Toast,
    ToggleSwitch,
    AdministrationNav,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './business-hours.html',
  styleUrl: './business-hours.scss',
})
export class BusinessHours {
  private readonly branchesService = inject(BranchesService);
  private readonly hoursService = inject(BusinessHoursService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  private submitAttempted = false;

  readonly descriptionMessage = DESCRIPTION_MESSAGE;
  readonly branchGoneMessage = BRANCH_GONE_MESSAGE;
  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly readOnlyMessage = READ_ONLY_MESSAGE;
  readonly branchesErrorMessage = BRANCHES_ERROR_MESSAGE;
  readonly hoursErrorMessage = HOURS_ERROR_MESSAGE;
  readonly emptyMessage = EMPTY_MESSAGE;
  readonly infoMessage = INFO_MESSAGE;
  readonly schedulingNote = SCHEDULING_NOTE;
  readonly days = BUSINESS_HOURS_DAYS;
  readonly parts = PARTS;
  readonly skeletonRows = BUSINESS_HOURS_DAYS;

  readonly branchControl = new FormControl<string | null>(null);

  readonly branchesLoading = signal(true);
  readonly branchesError = signal<ApiError | null>(null);
  readonly branches = signal<readonly BranchListItem[]>([]);
  readonly selectedBranchId = signal<string | null>(null);

  readonly hoursLoading = signal(false);
  readonly hoursError = signal<ApiError | null>(null);
  readonly body = signal<BusinessHoursBody | null>(null);
  readonly rows = signal<readonly BusinessHoursRow[]>([]);
  private readonly loadedRows = signal<readonly BusinessHoursRow[]>([]);

  readonly errors = signal<BusinessHoursErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly staleConflict = signal(false);
  /** BR-15: a `404` leaves this notice while the branch list reloads. */
  readonly branchGone = signal(false);
  readonly submitting = signal(false);
  readonly sessionExpired = signal(false);

  readonly forbidden = computed(
    () => this.branchesError()?.kind === 'forbidden' || this.hoursError()?.kind === 'forbidden',
  );
  readonly canManage = computed(() => this.body()?.canManage ?? false);
  readonly locked = computed(() => !this.canManage() || this.submitting());
  readonly dirty = computed(
    () => this.body() !== null && rowsSignature(this.rows()) !== rowsSignature(this.loadedRows()),
  );
  readonly canSave = computed(() => this.dirty() && !this.submitting());
  readonly showEditor = computed(() => this.body() !== null && !this.hoursLoading());

  readonly branchOptions = computed(() =>
    this.branches().map((branch) => ({
      id: branch.id,
      label: branch.isActive ? branch.name : `${branch.name} (Inactive)`,
    })),
  );
  readonly timezone = computed(() => {
    const body = this.body();
    const timezone =
      body !== null
        ? body.timezone
        : (this.branches().find((branch) => branch.id === this.selectedBranchId())?.timezone ??
          null);
    return timezoneLabel(timezone);
  });
  readonly optionsByKey = computed(() => {
    const result: Record<string, TimeOption[]> = {};
    for (const row of this.loadedRows()) {
      result[errorKey(row.day, 'openingTime')] = timeOptions(
        'openingTime',
        row.isOpen ? row.opening : null,
      );
      result[errorKey(row.day, 'closingTime')] = timeOptions(
        'closingTime',
        row.isOpen ? row.closing : null,
      );
    }
    return result;
  });

  constructor() {
    this.loadBranches();
  }

  dayLabel(day: BusinessHoursDayName): string {
    return BUSINESS_HOURS_DAY_LABELS[day];
  }

  id(day: BusinessHoursDayName, part: 'toggle' | BusinessHoursPart): string {
    return controlId(day, part);
  }

  error(day: BusinessHoursDayName, part: BusinessHoursPart): string | null {
    return this.errors()[errorKey(day, part)] ?? null;
  }

  options(day: BusinessHoursDayName, part: BusinessHoursPart): TimeOption[] {
    return this.optionsByKey()[errorKey(day, part)] ?? [];
  }

  /** Consulted by `businessHoursUnsavedChangesGuard` on route leave. */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired() || !this.dirty()) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      const answer = (value: boolean): void => {
        subscriber.next(value);
        subscriber.complete();
      };
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'these business hours',
          accept: () => answer(true),
          reject: () => answer(false),
        }),
      );
    });
  }

  onBranchChange(branchId: string | null): void {
    const current = this.selectedBranchId();
    if (branchId === null || branchId === current) {
      return;
    }
    if (!this.dirty()) {
      this.branchGone.set(false);
      this.selectBranch(branchId);
      return;
    }
    this.confirmationService.confirm(
      discardChangesConfirmation({
        subject: 'these business hours',
        accept: () => {
          this.branchGone.set(false);
          this.selectBranch(branchId);
        },
        reject: () => this.branchControl.setValue(current, { emitEvent: false }),
      }),
    );
  }

  setOpen(day: BusinessHoursDayName, isOpen: boolean): void {
    // BR-10: closed rows show, and reopened rows propose, 08:00-17:00.
    this.updateRow(day, {
      isOpen,
      opening: DEFAULT_OPENING,
      closing: DEFAULT_CLOSING,
    });
  }

  setTime(day: BusinessHoursDayName, part: BusinessHoursPart, value: string | null): void {
    this.updateRow(day, part === 'openingTime' ? { opening: value } : { closing: value });
  }

  cancel(): void {
    this.rows.set(this.loadedRows());
    this.errors.set({});
    this.pageMessage.set(null);
    this.staleConflict.set(false);
    this.submitAttempted = false;
  }

  save(): void {
    const body = this.body();
    if (body === null || !this.canSave()) {
      return;
    }
    this.submitAttempted = true;
    this.pageMessage.set(null);
    this.branchGone.set(false);
    this.staleConflict.set(false);

    const errors = validateRows(this.rows());
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusFirstInvalid();
      return;
    }

    this.submitting.set(true);
    this.hoursService
      .update(body.branchId, buildRequest(this.rows(), body.updatedAt))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.submitting.set(false);
          this.adopt(response);
          this.messageService.add({
            severity: 'success',
            summary: `Business hours saved for ${response.branchName}`,
          });
        },
        error: (error: unknown) => this.handleSaveFailed(error),
      });
  }

  /** Stale `409`: fetches the latest hours and clears the unsaved edits. */
  reloadStale(): void {
    const id = this.selectedBranchId();
    if (id !== null) {
      this.loadHours(id);
    }
  }

  retryHours(): void {
    this.reloadStale();
  }

  retryBranches(): void {
    this.loadBranches();
  }

  private updateRow(day: BusinessHoursDayName, change: Partial<BusinessHoursRow>): void {
    if (this.locked()) {
      return;
    }
    const next = this.rows().map((row) => (row.day === day ? { ...row, ...change } : row));
    this.rows.set(next);
    const updated = next.find((row) => row.day === day);
    if (updated === undefined) {
      return;
    }
    this.errors.update((errors) => {
      const rest = { ...errors };
      for (const part of PARTS) {
        delete rest[errorKey(day, part)];
      }
      return this.submitAttempted ? { ...rest, ...validateRow(updated) } : rest;
    });
  }

  private selectBranch(branchId: string): void {
    this.selectedBranchId.set(branchId);
    this.branchControl.setValue(branchId, { emitEvent: false });
    this.loadHours(branchId);
  }

  private adopt(response: BusinessHoursBody): void {
    const rows = rowsFromBody(response);
    this.body.set(response);
    this.rows.set(rows);
    this.loadedRows.set(rows);
    this.errors.set({});
    this.pageMessage.set(null);
    this.staleConflict.set(false);
    this.submitAttempted = false;
  }

  private loadBranches(): void {
    this.branchesLoading.set(true);
    this.branchesError.set(null);
    this.branchesService
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.branchesLoading.set(false);
          this.branches.set(response.items);
          const current = this.selectedBranchId();
          const next =
            current !== null && response.items.some((branch) => branch.id === current)
              ? current
              : defaultBranchId(response.items);
          if (next === null) {
            this.selectedBranchId.set(null);
            this.body.set(null);
            return;
          }
          this.selectBranch(next);
        },
        error: (error: unknown) => {
          this.branchesLoading.set(false);
          if (this.failedUnauthorized(error)) {
            return;
          }
          this.branchesError.set(isApiError(error) ? error : unknownError(BRANCHES_ERROR_MESSAGE));
        },
      });
  }

  private loadHours(branchId: string): void {
    this.hoursLoading.set(true);
    this.hoursError.set(null);
    this.staleConflict.set(false);
    this.hoursService
      .get(branchId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.hoursLoading.set(false);
          this.adopt(response);
        },
        error: (error: unknown) => {
          this.hoursLoading.set(false);
          if (this.failedUnauthorized(error)) {
            return;
          }
          this.body.set(null);
          if (isApiError(error) && error.kind === 'not-found') {
            this.branchGone.set(true);
            this.loadBranches();
            return;
          }
          this.hoursError.set(isApiError(error) ? error : unknownError(HOURS_ERROR_MESSAGE));
        },
      });
  }

  private handleSaveFailed(error: unknown): void {
    this.submitting.set(false);
    if (this.failedUnauthorized(error)) {
      return;
    }
    const apiError = isApiError(error) ? error : null;
    if (apiError?.kind === 'conflict') {
      this.staleConflict.set(true);
      this.pageMessage.set(STALE_MESSAGE);
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.branchGone.set(true);
      this.loadBranches();
      return;
    }
    if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const mapped = mapServerErrors(apiError.fieldErrors);
      this.errors.set(mapped);
      if (Object.keys(mapped).length > 0) {
        this.focusFirstInvalid();
        return;
      }
    }
    this.pageMessage.set(apiError?.message ?? 'An unexpected error occurred. Please try again.');
  }

  private failedUnauthorized(error: unknown): boolean {
    if (isApiError(error) && error.kind === 'unauthorized') {
      handleUnauthorized(this.router, this.sessionExpired);
      return true;
    }
    return false;
  }

  private focusFirstInvalid(): void {
    afterNextRender(
      () => {
        const errors = this.errors();
        for (const day of BUSINESS_HOURS_DAYS) {
          for (const part of PARTS) {
            if (errors[errorKey(day, part)] !== undefined) {
              this.hostElement.querySelector<HTMLElement>(`#${controlId(day, part)}`)?.focus();
              return;
            }
          }
        }
      },
      { injector: this.injector },
    );
  }
}

/** AS-02: the main branch when accessible, otherwise the first by name. */
function defaultBranchId(branches: readonly BranchListItem[]): string | null {
  const main = branches.find((branch) => branch.isMain);
  if (main !== undefined) {
    return main.id;
  }
  return [...branches].sort((a, b) => a.name.localeCompare(b.name))[0]?.id ?? null;
}

function unknownError(message: string): ApiError {
  return { kind: 'unknown', status: 0, message, fieldErrors: {} };
}
