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
import { Checkbox } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { TooltipModule } from 'primeng/tooltip';
import { Observable } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import {
  ErrorSummary,
  FieldErrorLink,
} from '../../../organizations/components/error-summary/error-summary';
import { FormField } from '../../../organizations/components/form-field/form-field';
import {
  AccessRequest,
  BranchRef,
  INVITE_FIELD_KEYS,
  InviteFieldErrors,
  InviteFieldKey,
  InviteRequest,
  MatrixRole,
  UserRow,
} from '../../models/users.model';
import { UsersService } from '../../services/users.service';
import {
  DELIVERY_FAILED_MESSAGE,
  LAST_OWNER_TOOLTIP,
  NOT_PENDING_MESSAGE,
  USER_UNAVAILABLE_MESSAGE,
  changeAccessMessage,
  isAccessReduced,
} from '../../utils/user-access';
import { fullName } from '../../utils/user-format';
import {
  EXPIRY_OPTIONS,
  FIELD_LABELS,
  InviteFormValue,
  mapServerFieldErrors,
  validateFields,
  validateInviteField,
} from './user-drawer.validators';

export type UserDrawerMode = 'invite' | 'edit';

const TITLE_ID = 'user-drawer-title';
const SUMMARY_ID = 'user-drawer-error-summary';
const EDIT_FIELDS: readonly InviteFieldKey[] = ['roleCode', 'branchIds'];
const CONTROL_IDS: Readonly<Record<InviteFieldKey, string>> = {
  email: 'invite-email',
  firstName: 'invite-first-name',
  lastName: 'invite-last-name',
  roleCode: 'invite-role',
  branchIds: 'invite-branch-all',
  expiresInDays: 'invite-expiry',
};

interface RoleOption {
  readonly code: string;
  readonly name: string;
  readonly disabled: boolean;
}

/**
 * Invite / Edit access drawer (FR-07, FR-10): one component, two modes. Owns the BR-19
 * branch and role rules, validation (BR-08), server-error mapping (BR-14) and the BR-12
 * reduction confirmation; the page owns refreshes and the 401 redirect.
 */
@Component({
  selector: 'app-user-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    Checkbox,
    InputText,
    Message,
    Select,
    SpinnerIcon,
    ToggleSwitch,
    TooltipModule,
    DrawerShell,
    ErrorSummary,
    FormField,
  ],
  templateUrl: './user-drawer.html',
  styleUrl: './user-drawer.scss',
})
export class UserDrawer {
  private readonly usersService = inject(UsersService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly open = input.required<boolean>();
  readonly mode = input.required<UserDrawerMode>();
  /** The row being edited (edit mode). */
  readonly row = input<UserRow | null>(null);
  /** Active branches (`GET /branches`). */
  readonly branches = input.required<readonly BranchRef[]>();
  readonly roles = input.required<readonly MatrixRole[]>();

  readonly saved = output<void>();
  readonly closed = output<void>();
  /** Any request returned `401`; the page owns the redirect. */
  readonly unauthorized = output<void>();

  readonly titleId = TITLE_ID;
  readonly summaryId = SUMMARY_ID;
  readonly controlIds = CONTROL_IDS;
  readonly expiryOptions = EXPIRY_OPTIONS.map((days) => ({ days, label: `${days} days` }));
  readonly lastOwnerTooltip = LAST_OWNER_TOOLTIP;

  readonly email = signal('');
  readonly firstName = signal('');
  readonly lastName = signal('');
  readonly roleCode = signal('');
  readonly isAllBranches = signal(false);
  private readonly branchIds = signal<readonly string[]>([]);
  readonly linkTeamProfile = signal(false);
  readonly expiresInDays = signal<number | null>(7);

  readonly fieldErrors = signal<InviteFieldErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);

  private readonly snapshot = signal<string | null>(null);
  private submitAttempted = false;

  readonly isInvite = computed(() => this.mode() === 'invite');
  readonly title = computed(() => (this.isInvite() ? 'Invite user' : 'Edit access'));
  readonly submitLabel = computed(() => (this.isInvite() ? 'Send invitation' : 'Save access'));
  readonly selectedRole = computed(
    () => this.roles().find((role) => role.code === this.roleCode()) ?? null,
  );
  /** BR-19: Owner and Operations Manager always have every branch. */
  readonly branchesForced = computed(() => this.selectedRole()?.forcesAllBranches ?? false);
  readonly showTeamLink = computed(
    () => this.isInvite() && (this.selectedRole()?.hasTeamProfile ?? false),
  );
  readonly roleOptions = computed<RoleOption[]>(() => {
    const lockOthers = !this.isInvite() && (this.row()?.isLastOwner ?? false);
    return this.roles().map((role) => ({
      code: role.code,
      name: role.name,
      disabled: lockOthers && role.code !== this.row()?.roleCode,
    }));
  });
  readonly lockedToLastOwner = computed(
    () => !this.isInvite() && (this.row()?.isLastOwner ?? false),
  );
  readonly allChecked = computed(() => this.isAllBranches() || this.branchesForced());

  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    INVITE_FIELD_KEYS.filter((field) => this.fieldErrors()[field] !== undefined).map((field) => ({
      fieldId: CONTROL_IDS[field],
      label: FIELD_LABELS[field],
      message: this.fieldErrors()[field] as string,
    })),
  );

  private readonly currentKey = computed(() =>
    JSON.stringify([
      this.email(),
      this.firstName(),
      this.lastName(),
      this.roleCode(),
      this.isAllBranches(),
      this.branchIds(),
      this.linkTeamProfile(),
      this.expiresInDays(),
    ]),
  );
  readonly dirty = computed(
    () => this.snapshot() !== null && this.snapshot() !== this.currentKey(),
  );

  constructor() {
    effect(() => {
      if (this.open()) {
        const mode = this.mode();
        const row = this.row();
        untracked(() => this.initialize(mode, row));
      }
    });
  }

  error(field: InviteFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  fullName(row: UserRow): string {
    return fullName(row);
  }

  isBranchChecked(id: string): boolean {
    return this.allChecked() || this.branchIds().includes(id);
  }

  private initialize(mode: UserDrawerMode, row: UserRow | null): void {
    this.submitAttempted = false;
    this.submitting.set(false);
    this.fieldErrors.set({});
    this.pageMessage.set(null);

    if (mode === 'edit' && row !== null) {
      this.email.set(row.email);
      this.firstName.set(row.firstName);
      this.lastName.set(row.lastName);
      this.roleCode.set(row.roleCode);
      this.isAllBranches.set(row.isAllBranches);
      this.branchIds.set(row.isAllBranches ? [] : row.branches.map((branch) => branch.id));
      this.linkTeamProfile.set(false);
      this.expiresInDays.set(7);
    } else {
      this.email.set('');
      this.firstName.set('');
      this.lastName.set('');
      this.roleCode.set('');
      this.isAllBranches.set(false);
      this.branchIds.set([]);
      this.linkTeamProfile.set(false);
      this.expiresInDays.set(7);
    }
    this.snapshot.set(this.currentKey());
  }

  /** BR-19: role change updates the summary and forces All branches for Owner / Operations Manager. */
  onRoleChange(code: string): void {
    const wasForced = this.branchesForced();
    this.roleCode.set(code);
    if (this.branchesForced()) {
      this.isAllBranches.set(true);
      this.branchIds.set([]);
    } else if (wasForced) {
      this.isAllBranches.set(false);
      this.branchIds.set([]);
    }
    this.revalidate('roleCode');
    this.revalidate('branchIds');
  }

  /** BR-19: All branches checks every branch; unchecking it clears them. */
  onAllBranchesChange(checked: boolean): void {
    if (this.branchesForced()) {
      return;
    }
    this.isAllBranches.set(checked);
    this.branchIds.set([]);
    this.revalidate('branchIds');
  }

  /** BR-19: unchecking any branch unchecks All; checking every branch checks it. */
  onBranchChange(id: string, checked: boolean): void {
    if (this.branchesForced()) {
      return;
    }
    const all = this.branches().map((branch) => branch.id);
    const current = this.isAllBranches() ? all : this.branchIds();
    const next = checked
      ? [...current.filter((existing) => existing !== id), id]
      : current.filter((existing) => existing !== id);
    const everything = all.length > 0 && all.every((branchId) => next.includes(branchId));
    this.isAllBranches.set(everything);
    this.branchIds.set(everything ? [] : next);
    this.revalidate('branchIds');
  }

  onFieldInput(field: 'email' | 'firstName' | 'lastName', value: string): void {
    this[field].set(value);
    if (this.fieldErrors()[field] !== undefined) {
      this.revalidate(field);
    }
  }

  onExpiryChange(days: number | null): void {
    this.expiresInDays.set(days);
    this.revalidate('expiresInDays');
  }

  onFieldBlur(field: InviteFieldKey): void {
    if (this.submitting()) {
      return;
    }
    this.setFieldError(field, this.validate(field));
  }

  private revalidate(field: InviteFieldKey): void {
    if (this.fieldErrors()[field] !== undefined || this.submitAttempted) {
      this.setFieldError(field, this.validate(field));
    }
  }

  private formValue(): InviteFormValue {
    return {
      email: this.email(),
      firstName: this.firstName(),
      lastName: this.lastName(),
      roleCode: this.roleCode(),
      isAllBranches: this.allChecked(),
      branchIds: this.branchIds(),
      linkTeamProfile: this.linkTeamProfile(),
      expiresInDays: this.expiresInDays(),
    };
  }

  private validRoleCodes(): string[] {
    return this.roles().map((role) => role.code);
  }

  private validate(field: InviteFieldKey): string | null {
    return validateInviteField(field, this.formValue(), this.validRoleCodes());
  }

  private setFieldError(field: InviteFieldKey, message: string | null): void {
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

  private buildAccess(): AccessRequest {
    const all = this.allChecked();
    const available = new Set(this.branches().map((branch) => branch.id));
    const ids = this.branchIds().filter((id) => available.size === 0 || available.has(id));
    return { roleCode: this.roleCode(), isAllBranches: all, branchIds: all ? [] : ids };
  }

  submit(): void {
    if (this.submitting()) {
      return;
    }
    this.submitAttempted = true;
    this.pageMessage.set(null);

    const fields = this.isInvite() ? INVITE_FIELD_KEYS : EDIT_FIELDS;
    const errors = validateFields(fields, this.formValue(), this.validRoleCodes());
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusFirstInvalid();
      return;
    }

    const access = this.buildAccess();
    const row = this.row();
    if (!this.isInvite() && row !== null && isAccessReduced(row, access)) {
      this.confirmationService.confirm({
        header: 'Change access',
        message: changeAccessMessage(
          row,
          fullName(row),
          this.selectedRole()?.name ?? access.roleCode,
          access,
          this.branches(),
        ),
        defaultFocus: 'reject',
        acceptButtonProps: { label: 'Change access' },
        rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
        accept: () => this.send(access),
      });
      return;
    }
    this.send(access);
  }

  private send(access: AccessRequest): void {
    const row = this.row();
    let request$: Observable<UserRow>;
    if (this.isInvite()) {
      const body: InviteRequest = {
        ...access,
        email: this.email().trim().toLowerCase(),
        firstName: this.firstName().trim(),
        lastName: this.lastName().trim(),
        linkTeamProfile: this.showTeamLink() && this.linkTeamProfile(),
        expiresInDays: this.expiresInDays() ?? 7,
      };
      request$ = this.usersService.invite(body);
    } else if (row !== null) {
      request$ = this.usersService.updateAccess(row, access);
    } else {
      return;
    }

    this.submitting.set(true);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (saved) => this.handleSaved(saved, row),
      error: (error: unknown) => this.handleFailed(error, row),
    });
  }

  private handleSaved(saved: UserRow, row: UserRow | null): void {
    this.submitting.set(false);
    const summary = this.isInvite()
      ? `Invitation sent to ${saved.email}`
      : `Access updated for ${fullName(row ?? saved)}`;
    this.messageService.add({ severity: 'success', summary });
    this.closed.emit();
    this.saved.emit();
  }

  private handleFailed(error: unknown, row: UserRow | null): void {
    this.submitting.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;

    switch (apiError?.kind) {
      case 'unauthorized':
        this.unauthorized.emit();
        return;
      case 'not-found':
        this.messageService.add({ severity: 'error', summary: USER_UNAVAILABLE_MESSAGE });
        this.closed.emit();
        this.saved.emit();
        return;
      case 'conflict': {
        const mapped = mapServerFieldErrors(apiError.fieldErrors);
        if (Object.keys(mapped).length > 0) {
          this.fieldErrors.set(mapped);
          this.focusFirstInvalid();
          return;
        }
        if (row?.kind === 'invitation') {
          this.messageService.add({ severity: 'error', summary: NOT_PENDING_MESSAGE });
          this.closed.emit();
          this.saved.emit();
          return;
        }
        this.pageMessage.set(row?.isLastOwner ? LAST_OWNER_TOOLTIP : apiError.message);
        this.focusSummary();
        return;
      }
      case 'validation':
      case 'bad-request': {
        const mapped = mapServerFieldErrors(apiError.fieldErrors);
        this.fieldErrors.set(mapped);
        if (Object.keys(mapped).length > 0) {
          this.focusFirstInvalid();
        } else {
          this.pageMessage.set(apiError.message);
          this.focusSummary();
        }
        return;
      }
    }

    // 502 is the delivery-port failure (BR-11); any other failure keeps the input and shows its message.
    this.pageMessage.set(
      apiError?.status === 502
        ? DELIVERY_FAILED_MESSAGE
        : (apiError?.message ?? 'An unexpected error occurred. Please try again.'),
    );
    this.focusSummary();
  }

  /** Read by the page's route-leave guard. */
  isDirty(): boolean {
    return this.open() && this.dirty();
  }

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.dirty()) {
      this.confirmationService.confirm({
        header: 'Discard unsaved changes?',
        message: 'You have unsaved changes. Do you want to discard them?',
        defaultFocus: 'reject',
        acceptButtonProps: { label: 'Discard', severity: 'danger' },
        rejectButtonProps: { label: 'Keep editing', severity: 'secondary', outlined: true },
        accept: () => this.closed.emit(),
      });
      return;
    }
    this.closed.emit();
  }

  private focusFirstInvalid(): void {
    afterNextRender(
      () => {
        const first = this.hostElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        // Custom controls (p-checkbox) carry the aria state on their host; focus the inner input.
        const target = first?.matches('input,button,textarea,[tabindex]')
          ? first
          : (first?.querySelector<HTMLElement>('input,button') ?? null);
        if (target) {
          target.focus();
        } else {
          this.focusSummaryNow();
        }
      },
      { injector: this.injector },
    );
  }

  private focusSummary(): void {
    afterNextRender(() => this.focusSummaryNow(), { injector: this.injector });
  }

  private focusSummaryNow(): void {
    this.hostElement.querySelector<HTMLElement>(`#${SUMMARY_ID}`)?.focus();
  }
}
