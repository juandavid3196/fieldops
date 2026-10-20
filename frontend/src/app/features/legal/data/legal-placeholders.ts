import { environment } from '../../../../environments/environment';

// Official values of the legal pages. The support email comes from the environment.
export const LEGAL_EFFECTIVE_DATE = 'October 20, 2026';
export const LEGAL_COMPANY_NAME = 'FieldOps';
export const LEGAL_JURISDICTION = 'Colombia';
export const LEGAL_SUPPORT_EMAIL = environment.supportEmail;

export function legalMailto(subject: string): string {
  return `mailto:${LEGAL_SUPPORT_EMAIL}?subject=${encodeURIComponent(subject)}`;
}
