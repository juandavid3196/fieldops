import { LegalDocument } from '../models/legal.model';
import { LEGAL_SUPPORT_EMAIL } from './legal-placeholders';

import { localized, t } from '../../../core/i18n/translate';
export const PRIVACY_POLICY: LegalDocument = localized(() => ({
  title: t('legal.privacyPolicy.privacyPolicy'),
  lead: t('legal.privacyPolicy.howFieldopsCollectsUses'),
  highlight: t('legal.privacyPolicy.fieldopsDoesNotSell'),
  contact: {
    title: t('legal.privacyPolicy.privacyQuestions'),
    text: t('legal.privacyPolicy.ifYouHaveQuestions'),
    subject: t('legal.privacyPolicy.fieldopsPrivacyRequest'),
  },
  sections: [
    {
      id: 'overview',
      title: t('legal.privacyPolicy.overview'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.fieldopsIsAField'),
        },
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.whenAnOrganizationUses'),
        },
      ],
    },
    {
      id: 'information-we-collect',
      title: t('legal.privacyPolicy.informationWeCollect'),
      blocks: [
        {
          kind: 'list',
          items: [
            {
              term: t('legal.privacyPolicy.accountInformation'),
              text: t('legal.privacyPolicy.yourNameEmailAddress'),
            },
            {
              term: t('legal.privacyPolicy.organizationAndOperationalData'),
              text: t('legal.privacyPolicy.organizationSettingsBranchesTeam'),
            },
            {
              term: t('legal.privacyPolicy.customerAndJobData'),
              text: t('legal.privacyPolicy.customerRecordsPropertiesService'),
            },
            {
              term: t('legal.privacyPolicy.usageAndSecurityData'),
              text: t('legal.privacyPolicy.ipAddressBrowserAnd'),
            },
            {
              term: t('legal.privacyPolicy.supportCommunications'),
              text: t('legal.privacyPolicy.informationYouProvideWhen'),
            },
          ],
        },
      ],
    },
    {
      id: 'how-we-use-information',
      title: t('legal.privacyPolicy.howWeUseInformation'),
      blocks: [
        { kind: 'paragraph', text: t('legal.privacyPolicy.weUseInformationTo') },
        {
          kind: 'list',
          items: [
            t('legal.privacyPolicy.provideOperateAndImprove'),
            t('legal.privacyPolicy.authenticateUsersAndProtect'),
            t('legal.privacyPolicy.processRequestsAndTransactions'),
            t('legal.privacyPolicy.enableCommunicationBetweenAuthor'),
            t('legal.privacyPolicy.provideCustomerSupport'),
            t('legal.privacyPolicy.monitorPerformanceAndPrevent'),
            t('legal.privacyPolicy.maintainAuditAndSecurity'),
            t('legal.privacyPolicy.complyWithLegalObligations'),
          ],
        },
      ],
    },
    {
      id: 'how-information-is-shared',
      title: t('legal.privacyPolicy.howInformationIsShared'),
      blocks: [
        { kind: 'paragraph', text: t('legal.privacyPolicy.informationMayBeShared') },
        {
          kind: 'list',
          items: [
            t('legal.privacyPolicy.withAuthorizedMembersOf'),
            t('legal.privacyPolicy.withServiceProvidersThat'),
            t('legal.privacyPolicy.withIntegrationsEnabledBy'),
            t('legal.privacyPolicy.whenRequiredByLaw'),
            t('legal.privacyPolicy.asPartOfA'),
          ],
        },
        { kind: 'paragraph', text: t('legal.privacyPolicy.weDoNotSell') },
      ],
    },
    {
      id: 'data-retention',
      title: t('legal.privacyPolicy.dataRetention'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.weRetainInformationWhile'),
        },
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.organizationsMayRetainOr'),
        },
      ],
    },
    {
      id: 'security',
      title: t('legal.privacyPolicy.security'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.fieldopsUsesReasonableTechnical'),
        },
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.noSystemCanGuarantee'),
        },
      ],
    },
    {
      id: 'your-rights',
      title: t('legal.privacyPolicy.yourRights'),
      blocks: [
        { kind: 'paragraph', text: t('legal.privacyPolicy.dependingOnYourLocation') },
        {
          kind: 'list',
          items: [
            t('legal.privacyPolicy.accessYourPersonalInformation'),
            t('legal.privacyPolicy.correctInaccurateInformation'),
            t('legal.privacyPolicy.requestDeletion'),
            t('legal.privacyPolicy.restrictOrObjectTo'),
            t('legal.privacyPolicy.requestAPortableCopy'),
            t('legal.privacyPolicy.withdrawConsentWhereProcessing'),
          ],
        },
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.requestsInvolvingCustomerOr'),
        },
      ],
    },
    {
      id: 'cookies',
      title: t('legal.privacyPolicy.cookies'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.fieldopsUsesEssentialCookies'),
        },
      ],
    },
    {
      id: 'childrens-privacy',
      title: t('legal.privacyPolicy.childrensPrivacy'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.fieldopsIsIntendedFor'),
        },
      ],
    },
    {
      id: 'changes',
      title: t('legal.privacyPolicy.changesToThisPolicy'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.weMayUpdateThis'),
        },
      ],
    },
    {
      id: 'contact',
      title: t('legal.privacyPolicy.contact'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.privacyPolicy.forPrivacyQuestionsOr', {
            LEGAL_SUPPORT_EMAIL: LEGAL_SUPPORT_EMAIL,
          }),
        },
      ],
    },
  ],
}));
