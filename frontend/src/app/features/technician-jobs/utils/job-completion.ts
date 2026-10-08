import { isApiError } from '../../../core/models/api-error.model';
import { TechnicianVisitDetail } from '../models/technician-visits.model';
import { formatQuantity } from './job-progress';
import { NOT_PRIMARY_JOB_MESSAGE } from './technician-errors';

export type AckMethod = 'signed' | 'customer_absent' | 'customer_refused' | 'remote_confirmation';
export type AckField = 'signerName' | 'relationship' | 'signature' | 'comment' | 'reviewConfirmed';
export type AckErrors = Partial<Record<AckField, string>>;
export type ReviewSection = 'tasks' | 'materials' | 'photos' | 'notes';

export const COMPLETE_ERROR_MESSAGE = "We couldn't complete this job. Try again.";
export const STATUS_INVALID_COMPLETE_MESSAGE = "This job can't be completed in its current state.";
export const REQUIREMENTS_UNMET_MESSAGE =
  'Complete required tasks and add before and after photos before completing this job.';
export const SIGNATURE_AGAIN_MESSAGE = 'Capture the signature again.';
export const MAX_SIGNER_NAME = 180;
export const MAX_COMMENT = 1000;

export interface MethodOption {
  readonly code: AckMethod;
  readonly label: string;
  readonly description: string;
  readonly icon: string;
}

/** BR-05 methods, in display order. */
export const METHODS: readonly MethodOption[] = [
  {
    code: 'signed',
    label: 'Signature obtained',
    description: 'Customer signs on this device.',
    icon: 'pi-pencil',
  },
  {
    code: 'customer_absent',
    label: 'Customer not available',
    description: 'Customer was not present on site.',
    icon: 'pi-user',
  },
  {
    code: 'customer_refused',
    label: 'Customer declined to sign',
    description: 'Customer reviewed the work but declined to sign.',
    icon: 'pi-ban',
  },
  {
    code: 'remote_confirmation',
    label: 'Remote confirmation',
    description: 'Acknowledged via email or phone.',
    icon: 'pi-envelope',
  },
];

/** BR-06 relationship codes and labels. */
export const RELATIONSHIPS: readonly { readonly code: string; readonly label: string }[] = [
  { code: 'customer', label: 'Customer' },
  { code: 'family_member', label: 'Family member' },
  { code: 'tenant', label: 'Tenant' },
  { code: 'property_manager', label: 'Property manager' },
  { code: 'employee', label: 'Employee' },
  { code: 'other', label: 'Other' },
];

export interface MethodFields {
  readonly signerName: 'required' | 'optional' | null;
  readonly relationship: boolean;
  readonly signature: boolean;
  readonly comment: 'required' | 'optional';
  readonly reviewConfirmed: boolean;
  readonly commentLabel: string;
  readonly commentRequiredMessage: string;
}

/** Acknowledgment field rules table. */
export const FIELD_RULES: Readonly<Record<AckMethod, MethodFields>> = {
  signed: {
    signerName: 'required',
    relationship: true,
    signature: true,
    comment: 'optional',
    reviewConfirmed: true,
    commentLabel: 'Customer comment (optional)',
    commentRequiredMessage: '',
  },
  customer_absent: {
    signerName: null,
    relationship: false,
    signature: false,
    comment: 'required',
    reviewConfirmed: false,
    commentLabel: 'Reason',
    commentRequiredMessage: 'Enter the reason.',
  },
  customer_refused: {
    signerName: 'optional',
    relationship: false,
    signature: false,
    comment: 'required',
    reviewConfirmed: false,
    commentLabel: 'Reason',
    commentRequiredMessage: 'Enter the reason.',
  },
  remote_confirmation: {
    signerName: 'required',
    relationship: true,
    signature: false,
    comment: 'required',
    reviewConfirmed: true,
    commentLabel: 'Confirmation details',
    commentRequiredMessage: 'Enter how the customer confirmed.',
  },
};

export interface AckValues {
  readonly method: AckMethod;
  readonly signerName: string;
  readonly relationship: string;
  readonly comment: string;
  readonly reviewConfirmed: boolean;
  readonly hasSignature: boolean;
}

/** BR-06 client validation with the field-table messages; only fields the method allows are checked. */
export function validateAck(values: AckValues): AckErrors {
  const rules = FIELD_RULES[values.method];
  const errors: Record<string, string> = {};
  const name = values.signerName.trim();
  if (rules.signerName === 'required' && name === '') {
    errors['signerName'] = "Enter the signer's name.";
  } else if (rules.signerName !== null && name.length > MAX_SIGNER_NAME) {
    errors['signerName'] = 'Use 180 characters or fewer.';
  }
  if (rules.relationship && !RELATIONSHIPS.some((item) => item.code === values.relationship)) {
    errors['relationship'] = 'Select the relationship.';
  }
  if (rules.signature && !values.hasSignature) {
    errors['signature'] = "Add the customer's signature.";
  }
  const comment = values.comment.trim();
  if (rules.comment === 'required' && comment === '') {
    errors['comment'] = rules.commentRequiredMessage;
  } else if (comment.length > MAX_COMMENT) {
    errors['comment'] = 'Use 1000 characters or fewer.';
  }
  if (rules.reviewConfirmed && !values.reviewConfirmed) {
    errors['reviewConfirmed'] = 'Confirm that the customer reviewed the work.';
  }
  return errors;
}

/** Field order of the form, for focusing the first invalid one. */
export const FIELD_ORDER: readonly AckField[] = [
  'signerName',
  'relationship',
  'signature',
  'comment',
  'reviewConfirmed',
];

/** Multipart body with only the fields the method allows; optional empty text is omitted (BR-06). */
export function buildCompletionBody(values: AckValues, signature: Blob | null): FormData {
  const rules = FIELD_RULES[values.method];
  const body = new FormData();
  body.append('method', values.method);
  const name = values.signerName.trim();
  if (rules.signerName !== null && name !== '') {
    body.append('signerName', name);
  }
  if (rules.relationship) {
    body.append('relationship', values.relationship);
  }
  const comment = values.comment.trim();
  if (comment !== '') {
    body.append('comment', comment);
  }
  if (rules.reviewConfirmed) {
    body.append('reviewConfirmed', 'true');
  }
  if (rules.signature && signature !== null) {
    body.append('signature', signature, 'signature.png');
  }
  return body;
}

export interface MaterialLines {
  readonly lines: readonly string[];
  /** Rows beyond the three shown. */
  readonly more: number;
  readonly total: number;
}

/** BR-12: planned rows with used quantity above zero first, then additional materials. */
export function materialLines(visit: TechnicianVisitDetail): MaterialLines {
  const all = [
    ...visit.plannedMaterials
      .filter((material) => material.usedQuantity > 0)
      .map((material) => `${material.description} × ${formatQuantity(material.usedQuantity)}`),
    ...visit.additionalMaterials.map(
      (material) => `${material.description} × ${formatQuantity(material.quantity)}`,
    ),
  ];
  return { lines: all.slice(0, 3), more: Math.max(0, all.length - 3), total: all.length };
}

export type CompleteFailure =
  | { readonly kind: 'not-primary'; readonly message: string }
  | { readonly kind: 'conflict'; readonly message: string }
  | { readonly kind: 'fields'; readonly fields: readonly AckField[]; readonly message: string }
  | { readonly kind: 'other'; readonly message: string };

function fieldKey(key: string): AckField | null {
  const normalized = key.charAt(0).toLowerCase() + key.slice(1);
  return (FIELD_ORDER as readonly string[]).includes(normalized) ? (normalized as AckField) : null;
}

/** BR-16 / BR-17: maps a failed Complete job call by `status` and `code`; backend text is never shown. */
export function classifyCompleteFailure(error: unknown): CompleteFailure {
  if (isApiError(error)) {
    if (error.status === 403 && error.code === 'not_primary_technician') {
      return { kind: 'not-primary', message: NOT_PRIMARY_JOB_MESSAGE };
    }
    if (error.status === 409) {
      return {
        kind: 'conflict',
        message:
          error.code === 'completion_requirements_unmet'
            ? REQUIREMENTS_UNMET_MESSAGE
            : STATUS_INVALID_COMPLETE_MESSAGE,
      };
    }
    if (error.status === 400) {
      const fields = Object.keys(error.fieldErrors)
        .map(fieldKey)
        .filter((field): field is AckField => field !== null);
      if (fields.length > 0) {
        return { kind: 'fields', fields, message: COMPLETE_ERROR_MESSAGE };
      }
    }
  }
  return { kind: 'other', message: COMPLETE_ERROR_MESSAGE };
}

const SERVER_FIELD_FALLBACKS: Readonly<Record<AckField, string>> = {
  signerName: "Enter the signer's name.",
  relationship: 'Select the relationship.',
  signature: SIGNATURE_AGAIN_MESSAGE,
  comment: 'Use 1000 characters or fewer.',
  reviewConfirmed: 'Confirm that the customer reviewed the work.',
};

/** Fixed copy for a field the server rejected: the signature message, else the client message, else a fallback. */
export function serverFieldMessage(field: AckField, values: AckValues): string {
  return field === 'signature'
    ? SIGNATURE_AGAIN_MESSAGE
    : (validateAck(values)[field] ?? SERVER_FIELD_FALLBACKS[field]);
}
