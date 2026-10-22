import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Account & administration articles. Content describes only behavior FieldOps implements (BR-10). */
export const ACCOUNT_ADMINISTRATION_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.accountAdministrationArticles.yourAccount'),
    articles: [
      {
        slug: 'update-your-profile',
        title: t('help.accountAdministrationArticles.updateYourProfile'),
        description: t('help.accountAdministrationArticles.changeYourNamePhone'),
        icon: 'pi-user',
        introduction: t('help.accountAdministrationArticles.yourProfileShowsYour'),
        callout: t('help.accountAdministrationArticles.yourEmailAddressOrganization'),
        steps: [
          {
            text: t('help.accountAdministrationArticles.openYourNameAt'),
          },
          {
            text: t('help.accountAdministrationArticles.toChangeYourPhoto'),
          },
          {
            text: t('help.accountAdministrationArticles.editYourFirstName'),
          },
          { text: t('help.accountAdministrationArticles.selectSaveChangesSelect') },
        ],
        whatHappensNext: t('help.accountAdministrationArticles.yourUpdatedNameAnd'),
      },
      {
        slug: 'change-your-password',
        title: t('help.accountAdministrationArticles.changeYourPassword'),
        description: t('help.accountAdministrationArticles.keepYourAccountSecure'),
        icon: 'pi-lock',
        introduction: t('help.accountAdministrationArticles.changeYourPasswordFrom'),
        steps: [
          {
            text: t('help.accountAdministrationArticles.openYourNameAt2'),
          },
          { text: t('help.accountAdministrationArticles.enterYourCurrentPassword') },
          {
            text: t('help.accountAdministrationArticles.enterYourNewPassword'),
          },
          { text: t('help.accountAdministrationArticles.selectUpdatePassword') },
        ],
        whatHappensNext: t('help.accountAdministrationArticles.changingYourPasswordKeeps'),
      },
    ],
  },
  {
    label: t('help.accountAdministrationArticles.administration'),
    articles: [
      {
        slug: 'manage-users-and-permissions',
        title: t('help.accountAdministrationArticles.manageUsersAndPermissions'),
        description: t('help.accountAdministrationArticles.inviteUsersAndReview'),
        icon: 'pi-users',
        introduction: t('help.accountAdministrationArticles.usersPermissionsListsEveryone'),
        callout: t('help.accountAdministrationArticles.onlyOwnersCanInvite'),
        steps: [
          { text: t('help.accountAdministrationArticles.selectAdministrationInThe') },
          {
            text: t('help.accountAdministrationArticles.useTheFiltersTo'),
          },
          { text: t('help.accountAdministrationArticles.selectInviteUser') },
          {
            text: t('help.accountAdministrationArticles.enterTheEmailAddress'),
          },
          {
            text: t('help.accountAdministrationArticles.chooseHowLongThe'),
          },
          {
            text: t('help.accountAdministrationArticles.toChangeSomeoneLater'),
          },
        ],
        whatHappensNext: t('help.accountAdministrationArticles.thePersonReceivesAn'),
      },
      {
        slug: 'manage-technician-skills-and-availability',
        title: t('help.accountAdministrationArticles.manageTechnicianSkillsAnd'),
        description: t('help.accountAdministrationArticles.setTheSkillsAnd'),
        icon: 'pi-id-card',
        introduction: t('help.accountAdministrationArticles.technicianProfilesHoldThe'),
        callout: t('help.accountAdministrationArticles.ownersAndOperationsManagers'),
        steps: [
          {
            text: t('help.accountAdministrationArticles.toCreateAProfile'),
          },
          {
            text: t('help.accountAdministrationArticles.openATechnicianFrom'),
          },
          {
            text: t('help.accountAdministrationArticles.inOperationalSkillsSearch'),
          },
          {
            text: t('help.accountAdministrationArticles.inStandardWeeklyAvailability'),
          },
          {
            text: t('help.accountAdministrationArticles.inAvailabilityExceptionsSelect'),
          },
          { text: t('help.accountAdministrationArticles.selectSaveChanges') },
        ],
        whatHappensNext: t('help.accountAdministrationArticles.dispatchersCanCompareTechnicians'),
      },
      {
        slug: 'manage-customer-records',
        title: t('help.accountAdministrationArticles.manageCustomerRecords'),
        description: t('help.accountAdministrationArticles.findEditImportAnd'),
        icon: 'pi-address-book',
        introduction: t('help.accountAdministrationArticles.customersKeepsEveryCustomer'),
        callout: t('help.accountAdministrationArticles.ownersAndDispatchersCan'),
        steps: [
          {
            text: t('help.accountAdministrationArticles.selectCustomersInThe'),
          },
          {
            text: t('help.accountAdministrationArticles.selectNewCustomerTo'),
          },
          {
            text: t('help.accountAdministrationArticles.toAddManyAt'),
          },
          {
            text: t('help.accountAdministrationArticles.openTheRowMenu'),
          },
          {
            text: t('help.accountAdministrationArticles.onTheCustomerPage'),
          },
        ],
        whatHappensNext: t('help.accountAdministrationArticles.archivedCustomersMoveTo'),
      },
      {
        slug: 'invite-a-customer-to-the-portal',
        title: t('help.accountAdministrationArticles.inviteACustomerTo'),
        description: t('help.accountAdministrationArticles.giveACustomerAccess'),
        icon: 'pi-send',
        introduction: t('help.accountAdministrationArticles.theCustomerPortalLets'),
        callout: t('help.accountAdministrationArticles.ownersAndDispatchersCan2'),
        steps: [
          { text: t('help.accountAdministrationArticles.selectCustomersAndOpen') },
          {
            text: t('help.accountAdministrationArticles.inTheContactDetails'),
          },
          {
            text: t('help.accountAdministrationArticles.theCardShowsPortal'),
          },
          {
            text: t('help.accountAdministrationArticles.toStopAccessSelect'),
          },
        ],
        whatHappensNext: t('help.accountAdministrationArticles.theCustomerReceivesAn'),
      },
    ],
  },
]);
