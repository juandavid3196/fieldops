import { isApiError } from '../../../core/models/api-error.model';
import { ActiveVisitSummary } from '../models/technician-visits.model';

import { localized, t } from '../../../core/i18n/translate';
export type TechnicianLoadFailure =
  'forbidden' | 'inactive' | 'not-linked' | 'not-found' | 'failed';

export const FORBIDDEN_MESSAGE = (): string =>
  t('technicianJobs.technicianErrors.forbiddenMessage');
export const NOT_LINKED_MESSAGE = (): string =>
  t('technicianJobs.technicianErrors.notLinkedMessage');
export const INACTIVE_MESSAGE = (): string => t('technicianJobs.technicianErrors.inactiveMessage');

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
      return FORBIDDEN_MESSAGE();
    case 'inactive':
      return INACTIVE_MESSAGE();
    case 'not-linked':
      return NOT_LINKED_MESSAGE();
    default:
      return null;
  }
}

export const UPDATE_ERROR_MESSAGE = (): string =>
  t('technicianJobs.technicianErrors.updateErrorMessage');
export const NOT_PRIMARY_MESSAGE = (): string =>
  t('technicianJobs.technicianErrors.notPrimaryMessage');
export const STATUS_INVALID_MESSAGE = (): string =>
  t('technicianJobs.technicianErrors.statusInvalidMessage');

const CONFLICT_MESSAGES: Readonly<Record<string, string>> = localized(() => ({
  visit_status_invalid: STATUS_INVALID_MESSAGE(),
  visit_not_today: t('technicianJobs.technicianErrors.travelCanOnlyBe'),
  another_visit_active: t('technicianJobs.technicianErrors.youreAlreadyTravelingTo'),
}));

export const START_JOB_INVALID_MESSAGE = (): string =>
  t('technicianJobs.technicianErrors.startJobInvalidMessage');
export const NOT_PRIMARY_JOB_MESSAGE = (): string =>
  t('technicianJobs.technicianErrors.notPrimaryJobMessage');

export interface TravelFailure {
  readonly message: string;
  /** A conflict or a lost primary assignment means the shown visit is stale. */
  readonly reload: boolean;
  /** Active visit behind `another_visit_active` (BR-19); the UI shows it with Open active visit. */
  readonly activeVisit?: ActiveVisitSummary;
}

export const RESUME_INVALID_MESSAGE = (): string =>
  t('technicianJobs.technicianErrors.resumeInvalidMessage');

/** Resume reuses the travel classification, with its own copy for an invalid status. */
export function classifyResumeFailure(error: unknown): TravelFailure {
  const failure = classifyTravelFailure(error);
  return isApiError(error) && error.status === 409 && error.code === 'visit_status_invalid'
    ? { ...failure, message: RESUME_INVALID_MESSAGE() }
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
            ? START_JOB_INVALID_MESSAGE()
            : (CONFLICT_MESSAGES[error.code ?? ''] ?? UPDATE_ERROR_MESSAGE()),
        reload: true,
        ...(error.code === 'another_visit_active' && error.activeVisit !== undefined
          ? { activeVisit: error.activeVisit }
          : {}),
      };
    }
    if (error.status === 403 && error.code === 'not_primary_technician') {
      return {
        message: action === 'start-job' ? NOT_PRIMARY_JOB_MESSAGE() : NOT_PRIMARY_MESSAGE(),
        reload: true,
      };
    }
  }
  return { message: UPDATE_ERROR_MESSAGE(), reload: false };
}
