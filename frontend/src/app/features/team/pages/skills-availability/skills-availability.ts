import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { Toast } from 'primeng/toast';
import { Observable } from 'rxjs';

import { comingSoonPath } from '../../../../core/config/coming-soon-modules';
import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { AvailabilityExceptions } from '../../components/availability-exceptions/availability-exceptions';
import { OperationalSkills } from '../../components/operational-skills/operational-skills';
import { SkillCatalogDialog } from '../../components/skill-catalog-dialog/skill-catalog-dialog';
import { WeeklyAvailability } from '../../components/weekly-availability/weekly-availability';
import {
  CatalogSkill,
  DayForm,
  SkillRow,
  SkillsAvailability,
  UpcomingDay,
} from '../../models/skills-availability.model';
import { SkillsAvailabilityService } from '../../services/skills-availability.service';
import {
  DayErrors,
  copyMondayToWeekdays,
  dropInactive,
  formatUpcomingDate,
  minutesToHours,
  toDayForms,
  toSkillRows,
  toSkillsRequest,
  toWeeklyRequest,
  utilizationView,
  windowValid,
  windowsText,
  splitWeeklyErrors,
} from '../../utils/skills-availability';
import { PROFILE_STATUS_LABELS, formatClock, profileStatusSeverity } from '../../utils/team-format';

export const FORBIDDEN_MESSAGE = "You don't have access to Team.";
export const NOT_AVAILABLE_MESSAGE = "This technician profile isn't available.";
export const LOAD_ERROR_MESSAGE = "We couldn't load this technician's skills and availability.";
export const SAVE_FAILED_MESSAGE = "We couldn't save your changes. Try again.";
export const SAVE_CONFLICT_MESSAGE =
  'This technician was updated by someone else. Reload to see the latest changes.';
const EDIT_ROLES: readonly string[] = ['owner', 'operations_manager'];
const READ_ROLES: readonly string[] = [...EDIT_ROLES, 'dispatcher', 'technician'];

interface UpcomingView {
  readonly key: string;
  readonly label: string;
  readonly text: string | null;
  readonly unavailable: boolean;
  readonly limited: boolean;
}

/**
 * Technician skills & availability (`/team/:technicianId/skills-availability`, Design 23).
 * Owner/Operations Manager edit; Dispatcher and Technician read; other roles are forbidden with no
 * data requests (BR-21). Backend authorization is authoritative.
 */
@Component({
  selector: 'app-skills-availability',
  imports: [
    RouterLink,
    ButtonDirective,
    ConfirmDialog,
    DiscardChangesDialog,
    Message,
    Skeleton,
    Tag,
    Toast,
    AvailabilityExceptions,
    OperationalSkills,
    SkillCatalogDialog,
    WeeklyAvailability,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './skills-availability.html',
  styleUrl: './skills-availability.scss',
})
export class SkillsAvailabilityPage {
  private readonly service = inject(SkillsAvailabilityService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly messages = inject(MessageService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly params = toSignal(inject(ActivatedRoute).paramMap);
  private readonly exceptionsCard = viewChild(AvailabilityExceptions);
  private readonly catalogDialog = viewChild(SkillCatalogDialog);

  protected readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  protected readonly notAvailableMessage = NOT_AVAILABLE_MESSAGE;
  protected readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  protected readonly conflictMessage = SAVE_CONFLICT_MESSAGE;
  protected readonly saveFailedMessage = SAVE_FAILED_MESSAGE;
  protected readonly profilePath = comingSoonPath('technician-profile');
  protected readonly schedulePath = '/schedule';
  protected readonly historyPath = comingSoonPath('technician-job-history');
  protected readonly statusLabels = PROFILE_STATUS_LABELS;
  protected readonly statusSeverity = profileStatusSeverity;
  protected readonly matchingSteps = [
    'Work order defines required skills.',
    'Technician profile provides skills and availability.',
    'Dispatcher compares matches and assigns manually.',
  ];

  // Role UX (BR-21); backend authorization is authoritative.
  private readonly roleCode = computed(() => this.session.session()?.role.code ?? '');
  protected readonly canEdit = computed(() => EDIT_ROLES.includes(this.roleCode()));
  private readonly serverForbidden = signal(false);
  protected readonly forbidden = computed(
    () => !READ_ROLES.includes(this.roleCode()) || this.serverForbidden(),
  );

  readonly sessionExpired = signal(false);
  protected readonly technicianId = computed(() => this.params()?.get('technicianId') ?? '');
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly notFound = signal(false);
  protected readonly data = signal<SkillsAvailability | null>(null);
  protected readonly days = signal<readonly DayForm[]>([]);
  protected readonly skillRows = signal<readonly SkillRow[]>([]);
  protected readonly catalog = signal<readonly CatalogSkill[]>([]);
  protected readonly catalogOpen = signal(false);
  protected readonly saving = signal(false);
  protected readonly conflict = signal(false);
  protected readonly saveFailed = signal(false);
  protected readonly dayErrors = signal<DayErrors>({});
  protected readonly skillsError = signal<string | null>(null);

  protected readonly skeletons = [0, 1, 2];

  protected readonly name = computed(() => {
    const technician = this.data()?.technician;
    return technician ? `${technician.firstName} ${technician.lastName}`.trim() : '';
  });
  protected readonly initials = computed(() => {
    const technician = this.data()?.technician;
    return technician
      ? `${technician.firstName.charAt(0)}${technician.lastName.charAt(0)}`.toUpperCase()
      : '';
  });

  private readonly savedSignature = computed(() =>
    this.signature(this.savedDays(), this.savedRows()),
  );
  private readonly savedDays = computed(() => toDayForms(this.data()?.weeklyAvailability ?? []));
  private readonly savedRows = computed(() => toSkillRows(this.data()?.skills ?? []));
  protected readonly dirty = computed(
    () =>
      this.data() !== null &&
      this.signature(this.days(), this.skillRows()) !== this.savedSignature(),
  );
  protected readonly valid = computed(() => this.days().every((day) => windowValid(day)));
  protected readonly canSave = computed(
    () => this.dirty() && this.valid() && !this.saving() && !this.conflict(),
  );
  protected readonly readOnly = computed(() => !this.canEdit());

  protected readonly upcoming = computed<UpcomingView[]>(() => {
    const data = this.data();
    return (data?.upcoming ?? []).map((day) => this.upcomingView(day, data?.timezone ?? ''));
  });
  protected readonly capacity = computed(() => {
    const capacity = this.data()?.capacity;
    return {
      available: minutesToHours(capacity?.availableMinutes ?? 0),
      scheduled: minutesToHours(capacity?.scheduledMinutes ?? 0),
      remaining: minutesToHours(capacity?.remainingMinutes ?? 0),
      utilization: utilizationView(capacity?.utilizationPercent),
    };
  });
  protected readonly booked = computed(() => {
    const percent = this.data()?.today.bookedPercent;
    return typeof percent === 'number' ? `${percent}% booked` : '—';
  });

  constructor() {
    if (this.forbidden()) {
      this.loading.set(false);
    } else {
      this.load();
      if (this.canEdit()) {
        this.loadCatalog();
      }
    }
  }

  private signature(days: readonly DayForm[], rows: readonly SkillRow[]): string {
    return JSON.stringify([toWeeklyRequest(days), toSkillsRequest(rows)]);
  }

  private upcomingView(day: UpcomingDay, timezone: string): UpcomingView {
    const label =
      day.label === 'today'
        ? 'Today'
        : day.label === 'tomorrow'
          ? 'Tomorrow'
          : formatUpcomingDate(day.date);
    let text: string | null = null;
    if (day.state === 'available_now') {
      text = 'Available now';
    } else if (day.state === 'next_available' && day.nextAvailableAt) {
      text = `Next available ${formatClock(day.nextAvailableAt, timezone)}`;
    } else if (day.state === 'windows') {
      text = windowsText(day.windows);
    }
    return {
      key: `${day.label}-${day.date}`,
      label,
      text,
      unavailable: day.state === 'unavailable',
      limited: day.limited,
    };
  }

  // Loading

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.notFound.set(false);
    this.service
      .get(this.technicianId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.apply(data);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          if (!this.handleAccessFailure(error)) {
            this.loadFailed.set(true);
          }
        },
      });
  }

  /** BR-11: Reload after a conflict discards the form and shows the latest saved state. */
  protected reload(): void {
    this.conflict.set(false);
    this.saveFailed.set(false);
    this.dayErrors.set({});
    this.skillsError.set(null);
    this.load();
  }

  private apply(data: SkillsAvailability): void {
    this.data.set(data);
    this.days.set(toDayForms(data.weeklyAvailability));
    this.skillRows.set(toSkillRows(data.skills));
  }

  /** Exception changes refresh the derived sections without touching the unsaved form. */
  protected refreshDerived(): void {
    this.service
      .get(this.technicianId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) =>
          this.data.update((current) =>
            current
              ? {
                  ...current,
                  exceptions: fresh.exceptions,
                  upcoming: fresh.upcoming,
                  capacity: fresh.capacity,
                  today: fresh.today,
                }
              : fresh,
          ),
        error: (error: unknown) => {
          this.handleAccessFailure(error);
        },
      });
  }

  protected loadCatalog(): void {
    this.service
      .catalog()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (skills) => this.catalog.set(skills),
        error: (error: unknown) => {
          this.handleAccessFailure(error);
        },
      });
  }

  /** 401 leaves to Sign In, 403 shows the forbidden state, 404 the not-available state. */
  private handleAccessFailure(error: unknown): boolean {
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized') {
      this.onUnauthorized();
      return true;
    }
    if (kind === 'forbidden') {
      this.serverForbidden.set(true);
      return true;
    }
    if (kind === 'not-found') {
      this.notFound.set(true);
      return true;
    }
    return false;
  }

  protected onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  protected onUnavailable(): void {
    this.messages.add({ severity: 'error', summary: NOT_AVAILABLE_MESSAGE });
    this.refreshDerived();
  }

  // Editing (BR-08)

  protected copyMonday(): void {
    this.days.update((days) => copyMondayToWeekdays(days));
  }

  protected reset(): void {
    this.days.set(this.savedDays());
  }

  // Saving (BR-11, BR-22)

  protected save(): void {
    const data = this.data();
    if (data === null || !this.canSave()) {
      return;
    }
    this.saving.set(true);
    this.saveFailed.set(false);
    this.dayErrors.set({});
    this.skillsError.set(null);
    this.service
      .save(data.technician.id, {
        version: data.version,
        weeklyAvailability: toWeeklyRequest(this.days()),
        skills: toSkillsRequest(this.skillRows()),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.apply(saved);
          this.messages.add({ severity: 'success', summary: 'Changes saved.' });
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.handleSaveError(error);
        },
      });
  }

  private handleSaveError(error: unknown): void {
    if (this.handleAccessFailure(error)) {
      return;
    }
    if (isApiError(error) && error.kind === 'conflict') {
      this.conflict.set(true);
    } else if (isApiError(error) && error.kind === 'validation') {
      const { days, rest } = splitWeeklyErrors(error.fieldErrors);
      this.dayErrors.set(days);
      this.skillsError.set(rest['skills'] ?? null);
      if (Object.keys(days).length === 0 && !rest['skills']) {
        this.saveFailed.set(true);
      }
    } else {
      this.saveFailed.set(true);
    }
  }

  // Catalog (BR-17)

  protected onCatalogChange(skills: readonly CatalogSkill[]): void {
    this.catalog.set(skills);
    // BR-09: assignments to inactive skills are neither shown nor submitted; the server keeps them.
    const inactive = new Set(skills.filter((skill) => !skill.isActive).map((skill) => skill.id));
    if (!this.skillRows().some((row) => inactive.has(row.skillId))) {
      return;
    }
    this.data.update((data) =>
      data ? { ...data, skills: data.skills.filter((row) => !inactive.has(row.skillId)) } : data,
    );
    this.skillRows.update((rows) => dropInactive(rows, skills));
  }

  // Leaving (BR-22)

  /** Consulted by `skillsAvailabilityUnsavedChangesGuard`. */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired()) {
      return true;
    }
    const exceptionDialog = this.exceptionsCard()?.dialog();
    const catalogDialog = this.catalogDialog();
    const dialogDirty =
      (exceptionDialog?.visible() && exceptionDialog.dirty()) ||
      (catalogDialog?.visible() && catalogDialog.dirty());
    if (!this.dirty() && !dialogDirty) {
      return true;
    }
    return this.confirmDiscard(
      this.dirty() ? "this technician's availability and skills" : 'this dialog',
    );
  }

  private confirmDiscard(subject: string): Observable<boolean> {
    return new Observable<boolean>((subscriber) => {
      this.confirmation.confirm(
        discardChangesConfirmation({
          subject,
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
}
