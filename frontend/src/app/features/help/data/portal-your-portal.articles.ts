import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Customer portal articles: your portal. Content describes only behavior FieldOps implements (BR-10, BR-14). */
export const PORTAL_YOUR_PORTAL_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.portalYourPortalArticles.getStarted'),
    articles: [
      {
        slug: 'activate-your-portal-access',
        title: t('help.portalYourPortalArticles.activateYourPortalAccess'),
        description: t('help.portalYourPortalArticles.acceptYourInvitationAnd'),
        icon: 'pi-key',
        introduction: t('help.portalYourPortalArticles.yourServiceCompanyInvites'),
        callout: t('help.portalYourPortalArticles.ifThePageSays'),
        steps: [
          { text: t('help.portalYourPortalArticles.openTheInvitationEmail') },
          {
            text: t('help.portalYourPortalArticles.ifThePageAsks'),
          },
          {
            text: t('help.portalYourPortalArticles.enterAPasswordAnd'),
          },
          {
            text: t('help.portalYourPortalArticles.ifYouAlreadyHave'),
          },
          { text: t('help.portalYourPortalArticles.otherwiseSelectActivateAccess') },
        ],
        whatHappensNext: t('help.portalYourPortalArticles.yourAccessIsReady'),
      },
      {
        slug: 'sign-in-and-reset-your-password',
        title: t('help.portalYourPortalArticles.signInAndReset'),
        description: t('help.portalYourPortalArticles.accessYourPortalOr'),
        icon: 'pi-sign-in',
        introduction: t('help.portalYourPortalArticles.signInWithThe'),
        callout: t('help.portalYourPortalArticles.thePortalHasNo'),
        steps: [
          { text: t('help.portalYourPortalArticles.openTheSignIn') },
          {
            text: t('help.portalYourPortalArticles.enterYourEmailAnd'),
          },
          {
            text: t('help.portalYourPortalArticles.selectRememberMeIf'),
          },
          { text: t('help.portalYourPortalArticles.selectSignIn') },
          {
            text: t('help.portalYourPortalArticles.ifYouForgotYour'),
          },
        ],
        whatHappensNext: t('help.portalYourPortalArticles.afterSigningInYou'),
      },
      {
        slug: 'get-around-your-portal',
        title: t('help.portalYourPortalArticles.getAroundYourPortal'),
        description: t('help.portalYourPortalArticles.findRequestsAppointmentsQuotes'),
        icon: 'pi-compass',
        introduction: t('help.portalYourPortalArticles.theTopBarOf'),
        callout: t('help.portalYourPortalArticles.onASmallScreen'),
        steps: [
          {
            text: t('help.portalYourPortalArticles.useHomeRequestsAppointments'),
          },
          {
            text: t('help.portalYourPortalArticles.onHomeReviewThe'),
          },
          {
            text: t('help.portalYourPortalArticles.selectHelpInThe'),
          },
          { text: t('help.portalYourPortalArticles.selectTheBellTo') },
          {
            text: t('help.portalYourPortalArticles.selectYourNameTo'),
          },
        ],
        whatHappensNext: t('help.portalYourPortalArticles.useHomeAsYour'),
      },
    ],
  },
  {
    label: t('help.portalYourPortalArticles.yourDetails'),
    articles: [
      {
        slug: 'manage-your-properties',
        title: t('help.portalYourPortalArticles.manageYourProperties'),
        description: t('help.portalYourPortalArticles.keepYourServiceLocations'),
        icon: 'pi-map-marker',
        introduction: t('help.portalYourPortalArticles.propertiesAreTheService'),
        callout: t('help.portalYourPortalArticles.youCannotChangeAn'),
        steps: [
          {
            text: t('help.portalYourPortalArticles.onTheYourProperty'),
          },
          { text: t('help.portalYourPortalArticles.toAddOneSelect') },
          {
            text: t('help.portalYourPortalArticles.enterAPropertyName'),
          },
          { text: t('help.portalYourPortalArticles.selectAddPropertyTo') },
          {
            text: t('help.portalYourPortalArticles.toUpdateAnExisting'),
          },
        ],
        whatHappensNext: t('help.portalYourPortalArticles.thePropertyAppearsIn'),
      },
      {
        slug: 'follow-your-updates-and-activity',
        title: t('help.portalYourPortalArticles.followYourUpdatesAnd'),
        description: t('help.portalYourPortalArticles.seeWhatChangedAnd'),
        icon: 'pi-bell',
        introduction: t('help.portalYourPortalArticles.messagesUpdatesIsA'),
        callout: t('help.portalYourPortalArticles.messagesUpdatesIsA2'),
        steps: [
          {
            text: t('help.portalYourPortalArticles.selectTheBellOr'),
          },
          {
            text: t('help.portalYourPortalArticles.useTheFiltersAll'),
          },
          { text: t('help.portalYourPortalArticles.typeInSearchUpdates') },
          {
            text: t('help.portalYourPortalArticles.selectTheButtonOn'),
          },
          {
            text: t('help.portalYourPortalArticles.toReviewPastWork'),
          },
        ],
        whatHappensNext: t('help.portalYourPortalArticles.unreadUpdatesAreMarked'),
      },
    ],
  },
]);
