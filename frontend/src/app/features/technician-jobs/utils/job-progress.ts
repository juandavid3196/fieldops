import { isApiError } from '../../../core/models/api-error.model';
import { TechnicianVisitDetail, VisitTask, VisitTime } from '../models/technician-visits.model';
import { NOT_PRIMARY_JOB_MESSAGE, UPDATE_ERROR_MESSAGE } from './technician-errors';

export const STATUS_EDIT_MESSAGE = "This job can't be updated in its current state.";
export const PAUSE_INVALID_MESSAGE = "This job can't be paused or resumed in its current state.";
export const NOTES_SAVE_ERROR_MESSAGE = "We couldn't save your notes. Try again.";
export const PHOTO_INVALID_MESSAGE = 'Upload a JPEG or PNG image up to 10 MB.';
export const QUANTITY_INVALID_MESSAGE =
  'Enter a quantity from 0 to 99999.999 with up to 3 decimals.';
export const MAX_QUANTITY = 99999.999;

const LIMIT_MESSAGES: Readonly<Record<string, string>> = {
  task_limit_reached: 'This job already has the maximum number of tasks.',
  material_limit_reached: 'This job already has the maximum number of materials.',
  evidence_limit_reached: 'This job already has the maximum number of photos.',
};

/** Whole seconds elapsed since an ISO instant, never negative (client clock skew). */
export function elapsedSeconds(startedAt: string, now: number): number {
  return Math.max(0, Math.floor((now - new Date(startedAt).getTime()) / 1000));
}

/** Closed work time plus the open work entry. */
export function laborSeconds(time: VisitTime, now: number): number {
  const active = time.activeEntry;
  return time.workSeconds + (active?.type === 'work' ? elapsedSeconds(active.startedAt, now) : 0);
}

/** Closed pause time plus the open pause entry. */
export function breakSeconds(time: VisitTime, now: number): number {
  const active = time.activeEntry;
  return time.pauseSeconds + (active?.type === 'pause' ? elapsedSeconds(active.startedAt, now) : 0);
}

/** BR-15 labor time: "<h>h <m>m", or "<m>m" under one hour. */
export function formatLabor(seconds: number): string {
  const minutes = Math.floor(Math.max(0, seconds) / 60);
  const hours = Math.floor(minutes / 60);
  return hours > 0 ? `${hours}h ${minutes % 60}m` : `${minutes}m`;
}

/** BR-18 estimate: "<h>h", "<h>h <m>m" or "<m>m". */
export function formatEstimate(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) {
    return `${rest}m`;
  }
  return rest === 0 ? `${hours}h` : `${hours}h ${rest}m`;
}

export type DeviationKind = 'under' | 'over' | 'on';

export interface Deviation {
  readonly kind: DeviationKind;
  readonly text: string;
}

/** BR-18 chip: whole labor minutes (rounded down) against the estimate; hidden without one. */
export function deviationFor(labor: number, estimatedMinutes: number | null): Deviation | null {
  if (estimatedMinutes === null) {
    return null;
  }
  const minutes = Math.floor(Math.max(0, labor) / 60);
  if (minutes < estimatedMinutes) {
    return { kind: 'under', text: `${estimatedMinutes - minutes} min under estimate` };
  }
  return minutes > estimatedMinutes
    ? { kind: 'over', text: `${minutes - estimatedMinutes} min over estimate` }
    : { kind: 'on', text: 'On estimate' };
}

export interface MutationFailure {
  readonly message: string;
  /** A conflict or a lost primary assignment means the shown visit is stale. */
  readonly reload: boolean;
}

export interface FailureMessages {
  /** Fixed copy for a `400` (and `413`); defaults to the generic message. */
  readonly invalid?: string;
  /** Fixed copy for `409 visit_status_invalid`. */
  readonly conflict?: string;
}

/** BR-19: 409 inline message and reload; 400 field message; anything else keeps local input. */
export function classifyMutationFailure(
  error: unknown,
  messages: FailureMessages = {},
): MutationFailure {
  if (isApiError(error)) {
    if (error.status === 409) {
      return {
        message: LIMIT_MESSAGES[error.code ?? ''] ?? messages.conflict ?? STATUS_EDIT_MESSAGE,
        reload: true,
      };
    }
    if (error.status === 403 && error.code === 'not_primary_technician') {
      return { message: NOT_PRIMARY_JOB_MESSAGE, reload: true };
    }
    if ((error.status === 400 || error.status === 413) && messages.invalid !== undefined) {
      return { message: messages.invalid, reload: false };
    }
  }
  return { message: UPDATE_ERROR_MESSAGE, reload: false };
}

/** Required tasks that are not complete yet. */
export function remainingRequired(tasks: readonly VisitTask[]): number {
  return tasks.filter((task) => task.isRequired && !task.isCompleted).length;
}

export function completionPercent(tasks: readonly VisitTask[]): number {
  return tasks.length === 0
    ? 0
    : Math.floor((tasks.filter((task) => task.isCompleted).length / tasks.length) * 100);
}

/** BR-16 remaining text. */
export function requiredText(remaining: number): string {
  if (remaining === 0) {
    return 'All required tasks complete';
  }
  return remaining === 1 ? '1 required task remaining' : `${remaining} required tasks remaining`;
}

export function hasEvidence(visit: TechnicianVisitDetail, type: 'before' | 'after'): boolean {
  return visit.evidence.some((item) => item.type === type);
}

/** BR-19: required tasks complete and at least one Before and one After photo. */
export function reviewReady(visit: TechnicianVisitDetail): boolean {
  return (
    remainingRequired(visit.tasks) === 0 &&
    hasEvidence(visit, 'before') &&
    hasEvidence(visit, 'after')
  );
}

/** Parses a typed quantity: 0–99999.999 with at most 3 decimals; `null` when invalid. */
export function parseQuantity(text: string): number | null {
  const value = text.trim().replace(',', '.');
  if (!/^\d+(\.\d{1,3})?$/.test(value)) {
    return null;
  }
  const parsed = Number(value);
  return parsed <= MAX_QUANTITY ? parsed : null;
}

/** Stepper step of one without floating point noise (3 decimals). */
export function stepQuantity(value: number, delta: number): number {
  return Math.min(MAX_QUANTITY, Math.max(0, Math.round((value + delta) * 1000) / 1000));
}

export function formatQuantity(value: number): string {
  return String(Math.round(value * 1000) / 1000);
}
