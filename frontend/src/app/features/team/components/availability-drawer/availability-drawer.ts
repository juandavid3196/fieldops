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
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Observable, Subscription } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import {
  ErrorSummary,
  FieldErrorLink,
} from '../../../organizations/components/error-summary/error-summary';
import {
  AvailabilityDay,
  DayOfWeekName,
  TechnicianAvailability,
} from '../../models/team-planning.model';
import { TeamOption } from '../../models/team.model';
import { TeamService } from '../../services/team.service';
import { DEFAULT_END, DEFAULT_START, toMinutes } from '../../utils/skills-availability';
import {
  DAY_KEYS,
  DAY_LABELS,
  END_BEFORE_START_MESSAGE,
  STALE_MESSAGE,
  TimeChoice,
  UNAVAILABLE_TECHNICIAN_MESSAGE,
  fieldMessages,
  hm,
  timeChoices,
} from '../../utils/team-planning';
import { TechnicianPicker } from '../technician-picker/technician-picker';

export const AVAILABILITY_SAVE_FAILED_MESSAGE = "We couldn't save availability. Try again.";
export const AVAILABILITY_LOAD_FAILED_MESSAGE = "We couldn't load this technician's availability.";
const TITLE_ID = 'availability-drawer-title';
const SUMMARY_ID = 'availability-drawer-summary';
const STATUS_CHOICES = [
  { value: true, label: 'Available' },
  { value: false, label: 'Unavailable' },
];

interface DayForm {
  readonly dayOfWeek: DayOfWeekName;
  readonly isAvailable: boolean;
  readonly start: string;
  readonly end: string;
}

interface DayErrors {
  start?: string;
  end?: string;
  day?: string;
}

export interface DayRowView {
  readonly day: DayForm;
  readonly label: string;
  readonly startChoices: TimeChoice[];
  readonly endChoices: TimeChoice[];
  readonly error: DayErrors;
}

const toForm = (days: readonly AvailabilityDay[]): DayForm[] =>
  DAY_KEYS.map((key) => {
    const day = days.find((candidate) => candidate.dayOfWeek === key);
    return {
      dayOfWeek: key,
      isAvailable: day?.isAvailable ?? false,
      start: hm(day?.start ?? null) ?? DEFAULT_START,
      end: hm(day?.end ?? null) ?? DEFAULT_END,
    };
  });

const toRequest = (days: readonly DayForm[]): AvailabilityDay[] =>
  days.map((day) => ({
    dayOfWeek: day.dayOfWeek,
    isAvailable: day.isAvailable,
    start: day.isAvailable ? day.start : null,
    end: day.isAvailable ? day.end : null,
  }));

/**
 * "Edit availability" drawer (BR-08..BR-12, BR-25): technician select, seven day rows, Use
 * branch hours, optimistic concurrency with Reload, dirty/discard and single submission.
 */
@Component({
  selector: 'app-availability-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    Message,
    Select,
    Skeleton,
    SpinnerIcon,
    DrawerShell,
    ErrorSummary,
    TechnicianPicker,
  ],
  templateUrl: './availability-drawer.html',
  styleUrl: './availability-drawer.scss',
})
export class AvailabilityDrawer {
  private readonly team = inject(TeamService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private loadRequest: Subscription | null = null;
  private saveRequest: Subscription | null = null;

  readonly open = input.required<boolean>();

  readonly closed = output<void>();
  readonly saved = output<void>();
  readonly unauthorized = output<void>();

  readonly titleId = TITLE_ID;
  readonly summaryId = SUMMARY_ID;
  readonly statusChoices = STATUS_CHOICES;
  readonly staleMessage = STALE_MESSAGE;
  readonly loadFailedMessage = AVAILABILITY_LOAD_FAILED_MESSAGE;
  readonly unavailableMessage = UNAVAILABLE_TECHNICIAN_MESSAGE;

  /** What the picker shows; `technicianId` is the technician whose data is loaded. */
  readonly selection = signal<string | null>(null);
  readonly technicianId = signal<string | null>(null);
  readonly detail = signal<TechnicianAvailability | null>(null);
  readonly days = signal<readonly DayForm[]>([]);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly notFound = signal(false);
  readonly submitting = signal(false);
  readonly stale = signal(false);
  readonly saveFailed = signal(false);
  private readonly serverErrors = signal<Readonly<Record<string, string>>>({});
  private readonly snapshot = signal<string | null>(null);

  readonly current = computed<TeamOption | null>(() => {
    const detail = this.detail();
    return detail === null
      ? null
      : { id: detail.technicianId, name: `${detail.firstName} ${detail.lastName}` };
  });
  readonly fullName = computed(() => this.current()?.name ?? '');
  readonly scheduleLabel = computed(() => {
    const detail = this.detail();
    return detail === null
      ? ''
      : detail.usesBranchHours
        ? `Using ${detail.branch.name} hours`
        : 'Custom schedule';
  });
  readonly rows = computed<readonly DayRowView[]>(() => {
    const loaded = this.detail()?.days ?? [];
    const errors = this.serverErrors();
    return this.days().map((day) => {
      const original = loaded.find((candidate) => candidate.dayOfWeek === day.dayOfWeek);
      const key = `days.${day.dayOfWeek}`;
      const error: DayErrors = {
        start: errors[`${key}.start`],
        end:
          (day.isAvailable && toMinutes(day.end) <= toMinutes(day.start)
            ? END_BEFORE_START_MESSAGE
            : undefined) ?? errors[`${key}.end`],
        day: errors[key] ?? errors[`${key}.isavailable`],
      };
      return {
        day,
        label: DAY_LABELS[day.dayOfWeek],
        startChoices: timeChoices(hm(original?.start ?? null)),
        endChoices: timeChoices(hm(original?.end ?? null)),
        error,
      };
    });
  });
  readonly valid = computed(() =>
    this.days().every((day) => !day.isAvailable || toMinutes(day.end) > toMinutes(day.start)),
  );
  readonly dirty = computed(
    () => this.snapshot() !== null && this.snapshot() !== JSON.stringify(toRequest(this.days())),
  );
  readonly busy = computed(() => this.submitting() || this.loading());
  readonly canSave = computed(
    () => this.detail() !== null && this.dirty() && this.valid() && !this.busy(),
  );
  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    this.rows().flatMap((row) => {
      const links: FieldErrorLink[] = [];
      const id = `availability-${row.day.dayOfWeek}`;
      if (row.error.day !== undefined) {
        links.push({ fieldId: `${id}-status`, label: row.label, message: row.error.day });
      }
      if (row.error.start !== undefined) {
        links.push({
          fieldId: `${id}-start`,
          label: `${row.label} start`,
          message: row.error.start,
        });
      }
      if (row.error.end !== undefined) {
        links.push({ fieldId: `${id}-end`, label: `${row.label} end`, message: row.error.end });
      }
      return links;
    }),
  );
  readonly hasServerErrors = computed(() => Object.keys(this.serverErrors()).length > 0);

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => this.reset());
      }
    });
    this.destroyRef.onDestroy(() => {
      this.loadRequest?.unsubscribe();
      this.saveRequest?.unsubscribe();
    });
  }

  private reset(): void {
    this.loadRequest?.unsubscribe();
    this.selection.set(null);
    this.technicianId.set(null);
    this.detail.set(null);
    this.days.set([]);
    this.snapshot.set(null);
    this.loading.set(false);
    this.loadFailed.set(false);
    this.notFound.set(false);
    this.submitting.set(false);
    this.clearMessages();
  }

  private clearMessages(): void {
    this.stale.set(false);
    this.saveFailed.set(false);
    this.serverErrors.set({});
  }

  // Technician selection (BR-08)

  onPick(id: string): void {
    if (id === this.technicianId() || this.submitting()) {
      this.selection.set(this.technicianId());
      return;
    }
    this.selection.set(id);
    if (!this.dirty()) {
      this.load(id);
      return;
    }
    this.confirmation.confirm(
      discardChangesConfirmation({
        subject: 'this availability',
        accept: () => this.load(id),
        reject: () => this.selection.set(this.technicianId()),
      }),
    );
  }

  load(id: string | null = this.technicianId()): void {
    if (id === null) {
      return;
    }
    this.loadRequest?.unsubscribe();
    this.technicianId.set(id);
    this.selection.set(id);
    this.loading.set(true);
    this.loadFailed.set(false);
    this.notFound.set(false);
    this.clearMessages();
    this.snapshot.set(null);
    this.loadRequest = this.team.technicianAvailability(id).subscribe({
      next: (detail) => {
        this.loading.set(false);
        this.apply(detail);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.detail.set(null);
        this.days.set([]);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.unauthorized.emit();
        } else if (kind === 'not-found') {
          this.notFound.set(true);
        } else {
          this.loadFailed.set(true);
        }
      },
    });
  }

  private apply(detail: TechnicianAvailability): void {
    this.detail.set(detail);
    this.days.set(toForm(detail.days));
    this.snapshot.set(JSON.stringify(toRequest(this.days())));
  }

  patch(dayOfWeek: DayOfWeekName, changes: Partial<DayForm>): void {
    this.days.update((days) =>
      days.map((day) => (day.dayOfWeek === dayOfWeek ? { ...day, ...changes } : day)),
    );
    this.serverErrors.set({});
  }

  // Save and Use branch hours (BR-10, BR-12)

  submit(): void {
    const detail = this.detail();
    if (!this.canSave() || detail === null) {
      return;
    }
    this.run(
      this.team.saveAvailability(detail.technicianId, {
        updatedAt: detail.updatedAt,
        days: toRequest(this.days()),
      }),
      'Availability saved.',
    );
  }

  useBranchHours(): void {
    const detail = this.detail();
    if (detail === null || detail.usesBranchHours || this.busy()) {
      return;
    }
    this.confirmation.confirm({
      header: `Use ${detail.branch.name} hours?`,
      message: `This removes ${this.fullName()}'s custom schedule.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Use branch hours' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.team.useBranchHours(detail.technicianId, detail.updatedAt),
          'Branch hours applied.',
        ),
    });
  }

  private run(request$: Observable<TechnicianAvailability>, success: string): void {
    if (this.submitting()) {
      return;
    }
    this.clearMessages();
    this.submitting.set(true);
    this.saveRequest = request$.subscribe({
      next: () => {
        this.submitting.set(false);
        this.messages.add({ severity: 'success', summary: success });
        this.saved.emit();
        this.closed.emit();
      },
      error: (error: unknown) => this.handleFailed(error),
    });
  }

  private handleFailed(error: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
    } else if (apiError?.kind === 'conflict') {
      this.stale.set(true);
    } else if (apiError?.kind === 'not-found') {
      this.detail.set(null);
      this.notFound.set(true);
    } else if (
      (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') &&
      Object.keys(apiError.fieldErrors).length > 0
    ) {
      this.serverErrors.set(fieldMessages(apiError.fieldErrors));
      afterNextRender(
        () =>
          (
            this.host.querySelector<HTMLElement>('[aria-invalid="true"]') ??
            this.host.querySelector<HTMLElement>(`#${SUMMARY_ID}`)
          )?.focus(),
        { injector: this.injector },
      );
    } else {
      this.saveFailed.set(true);
    }
  }

  // Close (BR-08)

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.dirty()) {
      this.confirmation.confirm(
        discardChangesConfirmation({
          subject: 'this availability',
          accept: () => this.closed.emit(),
        }),
      );
      return;
    }
    this.closed.emit();
  }
}
