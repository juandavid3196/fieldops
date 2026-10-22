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
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';
import { Subscription } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { FormField } from '../../../organizations/components/form-field/form-field';
import { TechnicianAvailability, TimeOff } from '../../models/team-planning.model';
import { TeamOption } from '../../models/team.model';
import { TeamService } from '../../services/team.service';
import { todayIn } from '../../utils/skills-availability';
import {
  UNAVAILABLE_TECHNICIAN_MESSAGE,
  fieldMessages,
  timeOffRange,
} from '../../utils/team-planning';
import { TechnicianPicker } from '../technician-picker/technician-picker';

import { t } from '../../../../core/i18n/translate';
export const OVERLAP_MESSAGE = (): string => t('team.timeOffDrawer.overlapMessage');
export const CLOSED_MESSAGE = (): string => t('team.timeOffDrawer.closedMessage');
export const TIME_OFF_STALE_MESSAGE = (): string => t('team.timeOffDrawer.timeOffStaleMessage');
export const PAST_START_MESSAGE = (): string => t('team.timeOffDrawer.pastStartMessage');
export const END_BEFORE_START_DATE_MESSAGE = (): string =>
  t('team.timeOffDrawer.endBeforeStartDateMessage');
export const NOTE_TOO_LONG_MESSAGE = (): string => t('team.timeOffDrawer.noteTooLongMessage');
const REQUIRED_MESSAGE = (): string => t('team.timeOffDrawer.requiredMessage');
const NOTE_MAX = 250;
const TITLE_ID = 'time-off-drawer-title';

interface TimeOffForm {
  readonly startDate: string;
  readonly endDate: string;
  readonly note: string;
}

type FieldKey = 'startDate' | 'endDate' | 'note';
type FormErrors = Partial<Record<FieldKey, string>>;

const EMPTY_FORM: TimeOffForm = { startDate: '', endDate: '', note: '' };

/** Pure validation of the add form (Time off validation table). */
export function validateTimeOff(form: TimeOffForm, today: string): FormErrors {
  const errors: FormErrors = {};
  if (form.startDate === '') {
    errors.startDate = REQUIRED_MESSAGE();
  } else if (form.startDate < today) {
    errors.startDate = PAST_START_MESSAGE();
  }
  if (form.endDate === '') {
    errors.endDate = REQUIRED_MESSAGE();
  } else if (form.startDate !== '' && form.endDate < form.startDate) {
    errors.endDate = END_BEFORE_START_DATE_MESSAGE();
  }
  if (form.note.trim().length > NOTE_MAX) {
    errors.note = NOTE_TOO_LONG_MESSAGE();
  }
  return errors;
}

/** "Time off" drawer (BR-13..BR-15, BR-25): upcoming list, remove with confirm, add form. */
@Component({
  selector: 'app-time-off-drawer',
  imports: [
    TranslocoPipe,
    FormsModule,
    ButtonDirective,
    InputText,
    Message,
    Skeleton,
    SpinnerIcon,
    Textarea,
    DrawerShell,
    FormField,
    TechnicianPicker,
  ],
  templateUrl: './time-off-drawer.html',
  styleUrl: './time-off-drawer.scss',
})
export class TimeOffDrawer {
  private readonly team = inject(TeamService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private loadRequest: Subscription | null = null;
  private mutation: Subscription | null = null;

  readonly open = input.required<boolean>();

  readonly closed = output<void>();
  /** A time off was added or removed; the grid refreshes. */
  readonly changed = output<void>();
  readonly unauthorized = output<void>();

  readonly titleId = TITLE_ID;
  readonly noteMax = NOTE_MAX;
  get unavailableMessage(): string {
    return UNAVAILABLE_TECHNICIAN_MESSAGE();
  }

  readonly selection = signal<string | null>(null);
  readonly technicianId = signal<string | null>(null);
  readonly detail = signal<TechnicianAvailability | null>(null);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly notFound = signal(false);
  readonly form = signal<TimeOffForm>(EMPTY_FORM);
  readonly errors = signal<FormErrors>({});
  readonly submitting = signal(false);
  readonly removingId = signal<string | null>(null);
  /** Inline `409` message; `reloadable` for the stale case. */
  readonly conflict = signal<{ readonly text: string; readonly reloadable: boolean } | null>(null);
  readonly saveFailed = signal(false);

  readonly current = computed<TeamOption | null>(() => {
    const detail = this.detail();
    return detail === null
      ? null
      : { id: detail.technicianId, name: `${detail.firstName} ${detail.lastName}` };
  });
  readonly items = computed(() =>
    (this.detail()?.timeOff ?? []).map((item) => ({
      item,
      range: timeOffRange(item.startDate, item.endDate),
    })),
  );
  readonly today = computed(() => {
    const zone = this.detail()?.timezone;
    return zone === undefined ? todayIn('UTC') : todayIn(zone);
  });
  readonly dirty = computed(() => {
    const form = this.form();
    return form.startDate !== '' || form.endDate !== '' || form.note !== '';
  });
  readonly busy = computed(() => this.submitting() || this.removingId() !== null);

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => this.reset());
      }
    });
    this.destroyRef.onDestroy(() => {
      this.loadRequest?.unsubscribe();
      this.mutation?.unsubscribe();
    });
  }

  private reset(): void {
    this.loadRequest?.unsubscribe();
    this.selection.set(null);
    this.technicianId.set(null);
    this.detail.set(null);
    this.loading.set(false);
    this.loadFailed.set(false);
    this.notFound.set(false);
    this.resetForm();
  }

  private resetForm(): void {
    this.form.set(EMPTY_FORM);
    this.errors.set({});
    this.conflict.set(null);
    this.saveFailed.set(false);
  }

  onPick(id: string): void {
    if (id === this.technicianId() || this.busy()) {
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
        subject: t('team.timeOffDrawer.thisTimeOff'),
        accept: () => this.load(id),
        reject: () => this.selection.set(this.technicianId()),
      }),
    );
  }

  /** `keepForm` refreshes only the list (after a change or a stale reload). */
  load(id: string | null = this.technicianId(), keepForm = false): void {
    if (id === null) {
      return;
    }
    this.loadRequest?.unsubscribe();
    this.technicianId.set(id);
    this.selection.set(id);
    this.loading.set(true);
    this.loadFailed.set(false);
    this.notFound.set(false);
    if (!keepForm) {
      this.resetForm();
    }
    this.conflict.set(null);
    this.loadRequest = this.team.technicianAvailability(id).subscribe({
      next: (detail) => {
        this.loading.set(false);
        this.detail.set(detail);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.detail.set(null);
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

  patch(changes: Partial<TimeOffForm>, field: FieldKey): void {
    this.form.update((current) => ({ ...current, ...changes }));
    if (this.errors()[field] !== undefined) {
      this.errors.set(validateTimeOff(this.form(), this.today()));
    }
    this.conflict.set(null);
  }

  // Add (BR-14)

  add(): void {
    const detail = this.detail();
    if (detail === null || this.busy() || this.loading()) {
      return;
    }
    const errors = validateTimeOff(this.form(), this.today());
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }
    const form = this.form();
    const note = form.note.trim();
    this.conflict.set(null);
    this.saveFailed.set(false);
    this.submitting.set(true);
    this.mutation = this.team
      .addTimeOff(detail.technicianId, {
        startDate: form.startDate,
        endDate: form.endDate,
        ...(note === '' ? {} : { note }),
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.messages.add({ severity: 'success', summary: t('team.timeOffDrawer.timeOffAdded') });
          this.resetForm();
          this.changed.emit();
          this.load(detail.technicianId, true);
        },
        error: (error: unknown) => this.handleFailed(error, false),
      });
  }

  // Remove (BR-15)

  confirmRemove(item: TimeOff): void {
    if (this.busy()) {
      return;
    }
    this.confirmation.confirm({
      header: t('team.timeOffDrawer.removeThisTimeOff'),
      message: t('team.timeOffDrawer.willBeRemovedFrom', {
        value: timeOffRange(item.startDate, item.endDate),
        value2: this.current()?.name ?? t('team.timeOffDrawer.thisTechnician'),
      }),
      defaultFocus: 'reject',
      acceptButtonProps: { label: t('common.remove') },
      rejectButtonProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
      accept: () => this.remove(item),
    });
  }

  private remove(item: TimeOff): void {
    const detail = this.detail();
    if (detail === null || this.busy()) {
      return;
    }
    this.conflict.set(null);
    this.removingId.set(item.id);
    this.mutation = this.team
      .removeTimeOff(detail.technicianId, item.id, item.updatedAt)
      .subscribe({
        next: () => {
          this.removingId.set(null);
          this.messages.add({
            severity: 'success',
            summary: t('team.timeOffDrawer.timeOffRemoved'),
          });
          this.changed.emit();
          this.load(detail.technicianId, true);
        },
        error: (error: unknown) => this.handleFailed(error, true),
      });
  }

  private handleFailed(error: unknown, removing: boolean): void {
    this.submitting.set(false);
    this.removingId.set(null);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
    } else if (apiError?.kind === 'not-found') {
      this.detail.set(null);
      this.notFound.set(true);
    } else if (apiError?.kind === 'conflict') {
      if (apiError.code === 'time_off_closed') {
        this.conflict.set({ text: CLOSED_MESSAGE(), reloadable: true });
      } else if (apiError.code === 'stale' || removing) {
        this.conflict.set({ text: TIME_OFF_STALE_MESSAGE(), reloadable: true });
      } else {
        this.conflict.set({ text: OVERLAP_MESSAGE(), reloadable: false });
      }
    } else if (
      !removing &&
      (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') &&
      Object.keys(apiError.fieldErrors).length > 0
    ) {
      const messages = fieldMessages(apiError.fieldErrors);
      this.errors.set({
        startDate: messages['startdate'],
        endDate: messages['enddate'],
        note: messages['note'],
      });
    } else {
      this.saveFailed.set(true);
    }
  }

  reload(): void {
    this.load(this.technicianId(), true);
  }

  requestClose(): void {
    if (this.busy()) {
      return;
    }
    if (this.dirty()) {
      this.confirmation.confirm(
        discardChangesConfirmation({
          subject: t('team.timeOffDrawer.thisTimeOff'),
          accept: () => this.closed.emit(),
        }),
      );
      return;
    }
    this.closed.emit();
  }
}
