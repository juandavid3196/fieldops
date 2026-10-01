import {
  Component,
  DestroyRef,
  ElementRef,
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
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';
import { Observable, Subscription } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import {
  ErrorSummary,
  FieldErrorLink,
} from '../../../organizations/components/error-summary/error-summary';
import { FormField } from '../../../organizations/components/form-field/form-field';
import {
  TECHNICIAN_FIELD_KEYS,
  TeamOption,
  TechnicianDetail,
  TechnicianFieldErrors,
  TechnicianFieldKey,
  TechnicianRequest,
} from '../../models/team.model';
import { TeamService } from '../../services/team.service';
import {
  EMPTY_FORM,
  FIELD_LABELS,
  TechnicianFormValue,
  mapServerFieldErrors,
  validateTechnician,
  validateTechnicianField,
} from './technician-form.validators';

export type TechnicianFormMode = 'create' | 'edit';

export const SAVE_FAILED_MESSAGE = "We couldn't save the technician profile. Try again.";
const TITLE_ID = 'technician-form-title';
const SUMMARY_ID = 'technician-form-summary';
const CONTROL_IDS: Readonly<Record<TechnicianFieldKey, string>> = {
  firstName: 'technician-first-name',
  lastName: 'technician-last-name',
  email: 'technician-email',
  phone: 'technician-phone',
  employeeCode: 'technician-code',
  branchId: 'technician-branch',
  notes: 'technician-notes',
};

const text = (value: string): string | null => (value.trim() === '' ? null : value.trim());

/**
 * "Add / Edit technician profile" drawer (Customers drawer pattern): field validation, server
 * field errors, the dirty-close guard and single submission (BR-15, BR-26). The page owns
 * refreshes and the 401 redirect.
 */
@Component({
  selector: 'app-technician-form-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    InputText,
    Message,
    Select,
    Skeleton,
    SpinnerIcon,
    Textarea,
    DrawerShell,
    ErrorSummary,
    FormField,
  ],
  templateUrl: './technician-form-drawer.html',
  styleUrl: './technician-form-drawer.scss',
})
export class TechnicianFormDrawer {
  private readonly team = inject(TeamService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly open = input.required<boolean>();
  readonly mode = input.required<TechnicianFormMode>();
  readonly technicianId = input<string | null>(null);
  readonly branches = input<readonly TeamOption[]>([]);
  readonly branchesLoading = input(false);

  readonly saved = output<string>();
  readonly closed = output<void>();
  readonly unauthorized = output<void>();
  /** The profile no longer exists (`404`). */
  readonly unavailable = output<void>();

  readonly titleId = TITLE_ID;
  readonly summaryId = SUMMARY_ID;
  readonly controlIds = CONTROL_IDS;

  readonly isCreate = computed(() => this.mode() === 'create');
  readonly title = computed(() =>
    this.isCreate() ? 'Add technician profile' : 'Edit technician profile',
  );

  readonly form = signal<TechnicianFormValue>(EMPTY_FORM);
  readonly fieldErrors = signal<TechnicianFieldErrors>({});
  readonly submitting = signal(false);
  readonly detailLoading = signal(false);
  readonly detailFailed = signal(false);
  private readonly currentBranch = signal<TeamOption | null>(null);
  private readonly snapshot = signal<string | null>(null);
  private submitAttempted = false;
  private detailRequest: Subscription | null = null;

  /** An unchanged inactive branch stays selectable on edit. */
  readonly branchOptions = computed<TeamOption[]>(() => {
    const options = this.branches();
    const current = this.currentBranch();
    return current !== null && !options.some((option) => option.id === current.id)
      ? [...options, current]
      : [...options];
  });
  private readonly branchIds = computed(() => this.branchOptions().map((option) => option.id));
  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    TECHNICIAN_FIELD_KEYS.filter((field) => this.fieldErrors()[field] !== undefined).map(
      (field) => ({
        fieldId: CONTROL_IDS[field],
        label: FIELD_LABELS[field],
        message: this.fieldErrors()[field] as string,
      }),
    ),
  );
  readonly dirty = computed(
    () => this.snapshot() !== null && this.snapshot() !== JSON.stringify(this.form()),
  );
  readonly saveBlocked = computed(
    () => this.submitting() || this.detailLoading() || this.detailFailed(),
  );

  constructor() {
    effect(() => {
      if (this.open()) {
        const mode = this.mode();
        const id = this.technicianId();
        untracked(() => this.initialize(mode, id));
      }
    });
    effect(() => {
      // A single available branch is preselected on create (branch options load after open).
      const options = this.branches();
      untracked(() => {
        if (
          this.open() &&
          this.isCreate() &&
          options.length === 1 &&
          this.form().branchId === null
        ) {
          this.form.update((current) => ({ ...current, branchId: options[0].id }));
          this.snapshot.set(JSON.stringify(this.form()));
        }
      });
    });
    this.destroyRef.onDestroy(() => this.detailRequest?.unsubscribe());
  }

  error(field: TechnicianFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  describedBy(field: TechnicianFieldKey): string | null {
    return this.error(field) !== null ? `${CONTROL_IDS[field]}-error` : null;
  }

  private initialize(mode: TechnicianFormMode, id: string | null): void {
    this.detailRequest?.unsubscribe();
    this.submitAttempted = false;
    this.submitting.set(false);
    this.fieldErrors.set({});
    this.detailFailed.set(false);
    this.currentBranch.set(null);
    const branches = this.branches();
    this.form.set({ ...EMPTY_FORM, branchId: branches.length === 1 ? branches[0].id : null });
    if (mode === 'create' || id === null) {
      this.detailLoading.set(false);
      this.snapshot.set(JSON.stringify(this.form()));
      return;
    }
    this.loadDetail(id);
  }

  loadDetail(id: string | null = this.technicianId()): void {
    if (id === null) {
      return;
    }
    this.detailRequest?.unsubscribe();
    this.detailLoading.set(true);
    this.detailFailed.set(false);
    this.snapshot.set(null);
    this.detailRequest = this.team.get(id).subscribe({
      next: (detail) => {
        this.detailLoading.set(false);
        this.currentBranch.set(detail.branch);
        this.form.set(this.fromDetail(detail));
        this.snapshot.set(JSON.stringify(this.form()));
      },
      error: (error: unknown) => {
        this.detailLoading.set(false);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.unauthorized.emit();
        } else if (kind === 'not-found') {
          this.unavailable.emit();
        } else {
          this.detailFailed.set(true);
        }
      },
    });
  }

  private fromDetail(detail: TechnicianDetail): TechnicianFormValue {
    return {
      firstName: detail.firstName,
      lastName: detail.lastName,
      email: detail.email ?? '',
      phone: detail.phone ?? '',
      employeeCode: detail.employeeCode ?? '',
      branchId: detail.branch.id,
      notes: detail.notes ?? '',
    };
  }

  patch(changes: Partial<TechnicianFormValue>, field: TechnicianFieldKey): void {
    this.form.update((current) => ({ ...current, ...changes }));
    if (this.fieldErrors()[field] !== undefined || this.submitAttempted) {
      this.setFieldError(field, this.validate(field));
    }
  }

  onFieldBlur(field: TechnicianFieldKey): void {
    if (!this.submitting()) {
      this.setFieldError(field, this.validate(field));
    }
  }

  private validate(field: TechnicianFieldKey): string | null {
    return validateTechnicianField(field, this.form(), this.branchIds());
  }

  private setFieldError(field: TechnicianFieldKey, message: string | null): void {
    this.fieldErrors.update((errors) => {
      const next = { ...errors };
      if (message === null) {
        delete next[field];
      } else {
        next[field] = message;
      }
      return next;
    });
  }

  submit(): void {
    if (this.saveBlocked()) {
      return;
    }
    this.submitAttempted = true;
    const value = this.form();
    const errors = validateTechnician(value, this.branchIds());
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusFirstInvalid();
      return;
    }
    const body: TechnicianRequest = {
      firstName: value.firstName.trim(),
      lastName: value.lastName.trim(),
      email: text(value.email),
      phone: text(value.phone),
      employeeCode: text(value.employeeCode),
      branchId: value.branchId as string,
      notes: text(value.notes),
    };
    const id = this.isCreate() ? null : this.technicianId();
    const request$: Observable<{ id: string } | TechnicianDetail> =
      id === null ? this.team.create(body) : this.team.update(id, body);

    this.submitting.set(true);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => {
        this.submitting.set(false);
        this.messageService.add({
          severity: 'success',
          summary: id === null ? 'Technician profile created.' : 'Technician profile updated.',
        });
        this.closed.emit();
        this.saved.emit(id ?? result.id);
      },
      error: (error: unknown) => this.handleFailed(error),
    });
  }

  private handleFailed(error: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.unavailable.emit();
      return;
    }
    if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const mapped = mapServerFieldErrors(apiError.fieldErrors);
      if (Object.keys(mapped).length > 0) {
        this.fieldErrors.set(mapped);
        this.focusFirstInvalid();
        return;
      }
    }
    this.messageService.add({ severity: 'error', summary: SAVE_FAILED_MESSAGE });
  }

  /** Read by the page's route-leave guard and drawer switching. */
  isDirty(): boolean {
    return this.open() && this.dirty();
  }

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.dirty()) {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this technician profile',
          accept: () => this.closed.emit(),
        }),
      );
      return;
    }
    this.closed.emit();
  }

  private focusFirstInvalid(): void {
    afterNextRender(
      () => {
        const target = this.hostElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        (target ?? this.hostElement.querySelector<HTMLElement>(`#${SUMMARY_ID}`))?.focus();
      },
      { injector: this.injector },
    );
  }
}
