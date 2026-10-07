import {
  ContactPreference,
  CustomerType,
  MaterialSource,
  PlannedMaterial,
  TechnicianVisitDetail,
  VisitTask,
} from '../models/technician-visits.model';
import { NOT_PRIMARY_MESSAGE, STATUS_INVALID_MESSAGE } from './technician-errors';
import { STATUS_LABELS, formatTime, pluralLabel } from './technician-format';

export type StepState = 'done' | 'current' | 'upcoming';

export interface Step {
  readonly label: string;
  readonly state: StepState;
}

const STEP_LABELS = ['Scheduled', 'On the way', 'Arrived', 'In progress'] as const;

/** Index of the current step; 4 means every step is done. */
function currentStep(visit: TechnicianVisitDetail): number {
  switch (visit.status) {
    case 'on_the_way':
      return visit.travel.arrivedAt === null ? 1 : 2;
    case 'in_progress':
    case 'paused':
      return 3;
    case 'completed':
    case 'needs_correction':
    case 'approved':
      return 4;
    default:
      return 0;
  }
}

/** BR-12 stepper. */
export function stepsFor(visit: TechnicianVisitDetail): Step[] {
  const current = currentStep(visit);
  return STEP_LABELS.map((label, index) => ({
    label,
    state: index < current ? 'done' : index === current ? 'current' : 'upcoming',
  }));
}

export interface Banner {
  readonly title: string;
  readonly detail: string | null;
  readonly note: string | null;
}

/** True once the open travel entry is closed (BR-04). */
export function hasArrived(visit: TechnicianVisitDetail): boolean {
  return visit.status === 'on_the_way' && visit.travel.arrivedAt !== null;
}

/** BR-12 status banner; `null` for `assigned` (and the equivalent `scheduled`). */
export function bannerFor(visit: TechnicianVisitDetail): Banner | null {
  const { travel, timezone } = visit;
  if (visit.status === 'on_the_way') {
    if (travel.arrivedAt !== null) {
      return {
        title: 'Arrived',
        detail: formatTime(travel.arrivedAt, timezone),
        note: travel.durationMinutes === null ? null : `Travel time ${travel.durationMinutes} min`,
      };
    }
    return {
      title: 'On the way',
      detail:
        travel.startedAt === null ? null : `Started ${formatTime(travel.startedAt, timezone)}`,
      note: null,
    };
  }
  if (visit.status === 'assigned' || visit.status === 'scheduled') {
    return null;
  }
  return { title: STATUS_LABELS[visit.status], detail: null, note: null };
}

/** Text announced politely after a successful action. */
export function announcementFor(visit: TechnicianVisitDetail): string {
  const banner = bannerFor(visit);
  return banner === null
    ? 'Job updated.'
    : [banner.title, banner.detail, banner.note].filter((part) => part !== null).join('. ') + '.';
}

export const START_JOB_AFTER_ARRIVAL = 'Available after arrival is recorded';

export interface ActionBar {
  /** Primary travel control; `arrived` is the disabled confirmation after arrival. */
  readonly travel: {
    readonly kind: 'start-travel' | 'arrive' | 'arrived';
    readonly label: string;
  } | null;
  /** Text shown instead of travel controls. */
  readonly message: string | null;
  /** Disabled Start job with its helper text; `null` hides it. */
  readonly startJobHelper: string | null;
  /** Enabled Start job (primary technician after arrival, BR-15). */
  readonly startJob: boolean;
}

/** BR-14 action bar by role and status; BR-15 enables Start job after arrival. */
export function actionBarFor(visit: TechnicianVisitDetail): ActionBar {
  const arrived = hasArrived(visit);
  const helper = START_JOB_AFTER_ARRIVAL;
  if (!visit.isPrimary) {
    return {
      travel: null,
      message: NOT_PRIMARY_MESSAGE,
      startJobHelper: arrived ? null : helper,
      startJob: false,
    };
  }
  if (visit.status === 'assigned') {
    return {
      travel: { kind: 'start-travel', label: 'Start travel' },
      message: null,
      startJobHelper: helper,
      startJob: false,
    };
  }
  if (visit.status === 'on_the_way') {
    const travel = arrived
      ? {
          kind: 'arrived' as const,
          label: `Arrived ${formatTime(visit.travel.arrivedAt!, visit.timezone)}`,
        }
      : { kind: 'arrive' as const, label: "I've arrived" };
    return {
      travel,
      message: null,
      startJobHelper: arrived ? null : helper,
      startJob: arrived,
    };
  }
  return { travel: null, message: STATUS_INVALID_MESSAGE, startJobHelper: null, startJob: false };
}

export function customerTypeLabel(type: CustomerType): string {
  return type === 'company' ? 'Commercial' : 'Residential';
}

const CONTACT_PREFERENCES: Readonly<Record<ContactPreference, string>> = {
  email: 'Email',
  sms: 'Text message',
  email_or_sms: 'Email or text message',
};

export function contactPreferenceText(preference: ContactPreference | null): string | null {
  return preference === null ? null : `Contact preference: ${CONTACT_PREFERENCES[preference]}`;
}

/** BR-16: non-empty lines with one leading `-`, `*` or `•` removed. */
export function scopeLines(scope: string): string[] {
  return scope
    .split(/\r?\n/)
    .map((line) =>
      line
        .trim()
        .replace(/^[-*•]\s*/, '')
        .trim(),
    )
    .filter((line) => line !== '');
}

const SOURCE_LABELS: Readonly<Record<MaterialSource, string>> = {
  truck_stock: 'Truck stock',
  warehouse: 'Warehouse',
  to_purchase: 'To purchase',
};

export function materialSourceLabel(source: MaterialSource): string {
  return SOURCE_LABELS[source];
}

export function materialLine(material: PlannedMaterial): string {
  return `${material.description} · ${material.quantity} ${material.unit}`;
}

/** "<n> planned materials" / "1 planned material". */
export function materialsCountText(count: number): string {
  return `${count} planned ${pluralLabel(count, 'material', 'materials')}`;
}

export function tasksSummary(tasks: readonly VisitTask[]): string {
  const completed = tasks.filter((task) => task.isCompleted).length;
  return `${completed} of ${tasks.length} tasks`;
}
