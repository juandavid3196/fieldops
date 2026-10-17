import { isApiError } from '../../../core/models/api-error.model';
import { ActiveVisitSummary } from '../models/technician-visits.model';

export type TechnicianLoadFailure =
  'forbidden' | 'inactive' | 'not-linked' | 'not-found' | 'failed';

export const FORBIDDEN_MESSAGE = "You don't have access to Today's jobs.";
export const NOT_LINKED_MESSAGE = "Your team profile isn't linked yet. Contact your manager.";
export const INACTIVE_MESSAGE = 'Your technician profile is inactive. Contact your manager.';

/** Maps a failed technician request to the page state; the backend code decides, never its text. */
export function classifyFailure(error: unknown): TechnicianLoadFailure {
  if (!isApiError(error)) {
    return 'failed';
  }
  if (error.status === 404) {
    return error.code === 'technician_profile_not_linked' ? 'not-linked' : 'not-found';
  }
  if (error.status === 403) {
    return error.code === 'technician_inactive' ? 'inactive' : 'forbidden';
  }
  return 'failed';
}

export function accessMessage(failure: TechnicianLoadFailure): string | null {
  switch (failure) {
    case 'forbidden':
      return FORBIDDEN_MESSAGE;
    case 'inactive':
      return INACTIVE_MESSAGE;
    case 'not-linked':
      return NOT_LINKED_MESSAGE;
    default:
      return null;
  }
}

export const UPDATE_ERROR_MESSAGE = "We couldn't update this job. Try again.";
export const NOT_PRIMARY_MESSAGE = 'The primary technician manages travel for this job.';
export const STATUS_INVALID_MESSAGE = 'Travel cannot be managed for this job in its current state.';

const CONFLICT_MESSAGES: Readonly<Record<string, string>> = {
  visit_status_invalid: STATUS_INVALID_MESSAGE,
  visit_not_today: 'Travel can only be started on the day of the visit.',
  another_visit_active: "You're already traveling to or working on another job.",
};

export const START_JOB_INVALID_MESSAGE = "This job can't be started in its current state.";
export const NOT_PRIMARY_JOB_MESSAGE = 'The primary technician manages this job.';

export interface TravelFailure {
  readonly message: string;
  /** A conflict or a lost primary assignment means the shown visit is stale. */
  readonly reload: boolean;
  /** Active visit behind `another_visit_active` (BR-19); the UI shows it with Open active visit. */
  readonly activeVisit?: ActiveVisitSummary;
}

export const RESUME_INVALID_MESSAGE = "This job can't be resumed in its current state.";

/** Resume reuses the travel classification, with its own copy for an invalid status. */
export function classifyResumeFailure(error: unknown): TravelFailure {
  const failure = classifyTravelFailure(error);
  return isApiError(error) && error.status === 409 && error.code === 'visit_status_invalid'
    ? { ...failure, message: RESUME_INVALID_MESSAGE }
    : failure;
}

/** Maps a failed Start travel / I've arrived call to its fixed copy by `status` and `code` (BR-14). */
export function classifyTravelFailure(
  error: unknown,
  action: 'travel' | 'start-job' = 'travel',
): TravelFailure {
  if (isApiError(error)) {
    if (error.status === 409) {
      return {
        message:
          action === 'start-job' && error.code === 'visit_status_invalid'
            ? START_JOB_INVALID_MESSAGE
            : (CONFLICT_MESSAGES[error.code ?? ''] ?? UPDATE_ERROR_MESSAGE),
        reload: true,
        ...(error.code === 'another_visit_active' && error.activeVisit !== undefined
          ? { activeVisit: error.activeVisit }
          : {}),
      };
    }
    if (error.status === 403 && error.code === 'not_primary_technician') {
      return {
        message: action === 'start-job' ? NOT_PRIMARY_JOB_MESSAGE : NOT_PRIMARY_MESSAGE,
        reload: true,
      };
    }
  }
  return { message: UPDATE_ERROR_MESSAGE, reload: false };
}
