import { environment } from '../../../../environments/environment';

import { t } from '../../../core/i18n/translate';
// Official values of the legal pages. The support email comes from the environment.
export const LEGAL_EFFECTIVE_DATE = (): string => t('legal.legalPlaceholders.legalEffectiveDate');
export const LEGAL_COMPANY_NAME = (): string => t('legal.legalPlaceholders.legalCompanyName');
export const LEGAL_JURISDICTION = (): string => t('legal.legalPlaceholders.legalJurisdiction');
export const LEGAL_SUPPORT_EMAIL = environment.supportEmail;

export function legalMailto(subject: string): string {
  return `mailto:${LEGAL_SUPPORT_EMAIL}?subject=${encodeURIComponent(subject)}`;
}
