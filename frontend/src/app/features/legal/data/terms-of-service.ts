import { LegalDocument } from '../models/legal.model';
import { LEGAL_COMPANY_NAME, LEGAL_JURISDICTION, LEGAL_SUPPORT_EMAIL } from './legal-placeholders';

import { localized, t } from '../../../core/i18n/translate';
export const TERMS_OF_SERVICE: LegalDocument = localized(() => ({
  title: t('legal.termsOfService.termsOfService'),
  lead: t('legal.termsOfService.theRulesForAccessing'),
  highlight: t('legal.termsOfService.byUsingFieldopsYou'),
  contact: {
    title: t('legal.termsOfService.questionsAboutTheseTerms'),
    text: t('legal.termsOfService.ifYouHaveQuestions'),
    subject: t('legal.termsOfService.fieldopsTermsQuestion'),
  },
  sections: [
    {
      id: 'agreement',
      title: t('legal.termsOfService.agreement'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.theseTermsGovernYour'),
        },
      ],
    },
    {
      id: 'accounts',
      title: t('legal.termsOfService.accounts'),
      blocks: [
        { kind: 'paragraph', text: t('legal.termsOfService.youMust') },
        {
          kind: 'list',
          items: [
            t('legal.termsOfService.provideAccurateAndCurrent'),
            t('legal.termsOfService.keepYourCredentialsSecure'),
            t('legal.termsOfService.useOnlyAccountsYou'),
            t('legal.termsOfService.promptlyReportUnauthorizedAccess'),
            t('legal.termsOfService.acceptResponsibilityForActivity'),
          ],
        },
      ],
    },
    {
      id: 'organization-responsibilities',
      title: t('legal.termsOfService.organizationResponsibilities'),
      blocks: [
        { kind: 'paragraph', text: t('legal.termsOfService.organizationsAreResponsibleFor') },
        {
          kind: 'list',
          items: [
            t('legal.termsOfService.managingUsersRolesPermissions'),
            t('legal.termsOfService.ensuringThatUsersAccess'),
            t('legal.termsOfService.collectingAndUsingCustomer'),
            t('legal.termsOfService.maintainingAccurateCustomerJob'),
            t('legal.termsOfService.obtainingAnyPermissionsRequired'),
          ],
        },
      ],
    },
    {
      id: 'acceptable-use',
      title: t('legal.termsOfService.acceptableUse'),
      blocks: [
        { kind: 'paragraph', text: t('legal.termsOfService.youMustNot') },
        {
          kind: 'list',
          items: [
            t('legal.termsOfService.violateApplicableLawsOr'),
            t('legal.termsOfService.accessAnotherOrganizationsInform'),
            t('legal.termsOfService.uploadMaliciousCodeOr'),
            t('legal.termsOfService.useFieldopsForFraudulent'),
            t('legal.termsOfService.circumventSecurityOrAccess'),
            t('legal.termsOfService.reverseEngineerOrAttempt'),
            t('legal.termsOfService.useTheServiceIn'),
          ],
        },
      ],
    },
    {
      id: 'your-data',
      title: t('legal.termsOfService.yourData'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.youRetainOwnershipOf'),
        },
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.youGrantPermissionTo', {
            LEGAL_COMPANY_NAME: LEGAL_COMPANY_NAME(),
          }),
        },
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.youAreResponsibleFor'),
        },
      ],
    },
    {
      id: 'fieldops-ownership',
      title: t('legal.termsOfService.fieldopsOwnership'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.fieldopsIncludingItsSoftware', {
            LEGAL_COMPANY_NAME: LEGAL_COMPANY_NAME(),
          }),
        },
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.theseTermsDoNot'),
        },
      ],
    },
    {
      id: 'service-availability',
      title: t('legal.termsOfService.serviceAvailability'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.weWorkToKeep'),
        },
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.featuresMayBeImproved'),
        },
      ],
    },
    {
      id: 'third-party-services',
      title: t('legal.termsOfService.thirdPartyServices'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.fieldopsMayConnectWith'),
        },
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.fieldopsIsNotResponsible'),
        },
      ],
    },
    {
      id: 'suspension-and-termination',
      title: t('legal.termsOfService.suspensionAndTermination'),
      blocks: [
        { kind: 'paragraph', text: t('legal.termsOfService.accessMayBeSuspended') },
        {
          kind: 'list',
          items: [
            t('legal.termsOfService.theseTermsAreMaterially'),
            t('legal.termsOfService.theServiceIsUsed'),
            t('legal.termsOfService.requiredPaymentsRemainOverdue'),
            t('legal.termsOfService.anOrganizationOrUser'),
          ],
        },
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.whereReasonableNoticeAnd'),
        },
      ],
    },
    {
      id: 'disclaimers',
      title: t('legal.termsOfService.disclaimers'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.fieldopsIsProvidedOn'),
        },
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.organizationsRemainResponsibleFo'),
        },
      ],
    },
    {
      id: 'limitation-of-liability',
      title: t('legal.termsOfService.limitationOfLiability'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.toTheExtentPermitted', {
            LEGAL_COMPANY_NAME: LEGAL_COMPANY_NAME(),
          }),
        },
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.nothingInTheseTerms'),
        },
      ],
    },
    {
      id: 'governing-law',
      title: t('legal.termsOfService.governingLaw'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.theseTermsAreGoverned', {
            LEGAL_JURISDICTION: LEGAL_JURISDICTION(),
          }),
        },
      ],
    },
    {
      id: 'changes',
      title: t('legal.termsOfService.changesToTheseTerms'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.weMayUpdateThese'),
        },
      ],
    },
    {
      id: 'contact',
      title: t('legal.termsOfService.contact'),
      blocks: [
        {
          kind: 'paragraph',
          text: t('legal.termsOfService.questionsAboutTheseTerms2', {
            LEGAL_SUPPORT_EMAIL: LEGAL_SUPPORT_EMAIL,
          }),
        },
      ],
    },
  ],
}));
