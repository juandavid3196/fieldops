import { isApiError } from '../../../core/models/api-error.model';

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
