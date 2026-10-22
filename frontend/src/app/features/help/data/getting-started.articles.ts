import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Getting started articles. Content describes only behavior FieldOps implements (BR-10). */
export const GETTING_STARTED_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.gettingStartedArticles.setUpYourWorkspace'),
    articles: [
      {
        slug: 'fieldops-basics',
        title: t('help.gettingStartedArticles.fieldopsBasics'),
        description: t('help.gettingStartedArticles.understandTheMainAreas'),
        icon: 'pi-book',
        introduction: t('help.gettingStartedArticles.fieldopsOrganizesYourField'),
        callout: t('help.gettingStartedArticles.theSidebarOnlyLists'),
        steps: [
          {
            text: t('help.gettingStartedArticles.startOnTheDashboard'),
          },
          {
            text: t('help.gettingStartedArticles.underOperationsUseRequests'),
          },
          {
            text: t('help.gettingStartedArticles.underPeopleUseCustomers'),
          },
          {
            text: t('help.gettingStartedArticles.underFinanceUseProducts'),
          },
          {
            text: t('help.gettingStartedArticles.useTheBellIcon'),
          },
        ],
        whatHappensNext: t('help.gettingStartedArticles.aTypicalJobMoves'),
      },
      {
        slug: 'set-up-your-organization',
        title: t('help.gettingStartedArticles.setUpYourOrganization'),
        description: t('help.gettingStartedArticles.reviewYourCompanyDetails'),
        icon: 'pi-building',
        introduction: t('help.gettingStartedArticles.companySettingsHoldThe'),
        callout: t('help.gettingStartedArticles.administrationIsAvailableTo'),
        steps: [
          { text: t('help.gettingStartedArticles.selectAdministrationInThe') },
          {
            text: t('help.gettingStartedArticles.inCompanyProfileReview'),
          },
          { text: t('help.gettingStartedArticles.inTaxesCurrencyChoose') },
          {
            text: t('help.gettingStartedArticles.inDocumentNumberingReview'),
          },
          {
            text: t('help.gettingStartedArticles.reviewBusinessHoursAnd'),
          },
        ],
        whatHappensNext: t('help.gettingStartedArticles.yourDefaultsAreUsed'),
      },
      {
        slug: 'create-and-manage-branches',
        title: t('help.gettingStartedArticles.createAndManageBranches'),
        description: t('help.gettingStartedArticles.organizeWorkByService'),
        icon: 'pi-map-marker',
        introduction: t('help.gettingStartedArticles.branchesLetYouOrganize'),
        steps: [
          { text: t('help.gettingStartedArticles.selectAdministrationThenOpen') },
          { text: t('help.gettingStartedArticles.selectAddBranchTo') },
          {
            text: t('help.gettingStartedArticles.enterTheBranchName'),
          },
          {
            text: t('help.gettingStartedArticles.turnOnUseCompany'),
          },
          { text: t('help.gettingStartedArticles.selectCreateBranch') },
          {
            text: t('help.gettingStartedArticles.toChangeABranch'),
          },
        ],
        whatHappensNext: t('help.gettingStartedArticles.theBranchAppearsIn'),
      },
    ],
  },
  {
    label: t('help.gettingStartedArticles.firstSteps'),
    articles: [
      {
        slug: 'invite-your-team',
        title: t('help.gettingStartedArticles.inviteYourTeam'),
        description: t('help.gettingStartedArticles.addStaffAndAssign'),
        icon: 'pi-users',
        introduction: t('help.gettingStartedArticles.inviteColleaguesByEmail'),
        callout: t('help.gettingStartedArticles.theLastOwnerCannot'),
        steps: [
          { text: t('help.gettingStartedArticles.selectAdministrationThenUsers') },
          { text: t('help.gettingStartedArticles.selectInviteUser') },
          { text: t('help.gettingStartedArticles.enterTheEmailAddress') },
          {
            text: t('help.gettingStartedArticles.chooseARoleThe'),
          },
          {
            text: t('help.gettingStartedArticles.chooseTheBranchAccess'),
          },
          { text: t('help.gettingStartedArticles.selectSendInvitation') },
        ],
        whatHappensNext: t('help.gettingStartedArticles.thePersonReceivesAn'),
      },
      {
        slug: 'add-your-first-customer',
        title: t('help.gettingStartedArticles.addYourFirstCustomer'),
        description: t('help.gettingStartedArticles.createACustomerAnd'),
        icon: 'pi-user',
        introduction: t('help.gettingStartedArticles.customersHoldContactDetails'),
        steps: [
          { text: t('help.gettingStartedArticles.selectCustomersInThe') },
          {
            text: t('help.gettingStartedArticles.chooseTheCustomerType'),
          },
          {
            text: t('help.gettingStartedArticles.addTheAddressService'),
          },
          {
            text: t('help.gettingStartedArticles.ifFieldopsShowsPossible'),
          },
          { text: t('help.gettingStartedArticles.selectCreateCustomer') },
          {
            text: t('help.gettingStartedArticles.openTheCustomerAnd'),
          },
        ],
        whatHappensNext: t('help.gettingStartedArticles.theCustomerAndIts'),
      },
      {
        slug: 'configure-products-and-services',
        title: t('help.gettingStartedArticles.configureProductsAndServices'),
        description: t('help.gettingStartedArticles.prepareReusableItemsFor'),
        icon: 'pi-box',
        introduction: t('help.gettingStartedArticles.theCatalogStoresThe'),
        steps: [
          { text: t('help.gettingStartedArticles.selectProductsAndServices') },
          { text: t('help.gettingStartedArticles.selectAddItem') },
          { text: t('help.gettingStartedArticles.chooseTheTypeThen') },
          {
            text: t('help.gettingStartedArticles.enterTheUnitPrice'),
          },
          { text: t('help.gettingStartedArticles.setTaxableAndActive') },
          {
            text: t('help.gettingStartedArticles.useManageCategoriesTo'),
          },
        ],
        whatHappensNext: t('help.gettingStartedArticles.activeItemsCanBe'),
      },
    ],
  },
]);
