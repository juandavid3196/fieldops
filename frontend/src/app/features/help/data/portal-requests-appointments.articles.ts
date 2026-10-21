import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Customer portal articles: requests and appointments. Content describes only behavior FieldOps implements (BR-10, BR-14). */
export const PORTAL_REQUESTS_APPOINTMENTS_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.portalRequestsAppointmentsArticles.requests'),
    articles: [
      {
        slug: 'request-a-service',
        title: t('help.portalRequestsAppointmentsArticles.requestAService'),
        description: t('help.portalRequestsAppointmentsArticles.tellUsWhatYou'),
        icon: 'pi-plus-circle',
        introduction: t('help.portalRequestsAppointmentsArticles.sendAServiceRequest'),
        callout: t('help.portalRequestsAppointmentsArticles.submittingARequestDoes'),
        steps: [
          { text: t('help.portalRequestsAppointmentsArticles.selectRequestAService') },
          {
            text: t('help.portalRequestsAppointmentsArticles.inPropertyChooseOne'),
          },
          {
            text: t('help.portalRequestsAppointmentsArticles.inServiceDetailsChoose'),
          },
          {
            text: t('help.portalRequestsAppointmentsArticles.inAvailabilityChooseAs'),
          },
          {
            text: t('help.portalRequestsAppointmentsArticles.inReviewCheckYour'),
          },
        ],
        whatHappensNext: t('help.portalRequestsAppointmentsArticles.youSeeYourRequest'),
      },
      {
        slug: 'request-a-service-without-signing-in',
        title: t('help.portalRequestsAppointmentsArticles.requestAServiceWithout'),
        description: t('help.portalRequestsAppointmentsArticles.useThePublicRequest'),
        icon: 'pi-globe',
        introduction: t('help.portalRequestsAppointmentsArticles.yourServiceCompanyCan'),
        callout: t('help.portalRequestsAppointmentsArticles.forImmediateDangerShut'),
        steps: [
          {
            text: t('help.portalRequestsAppointmentsArticles.openThePublicRequest'),
          },
          {
            text: t('help.portalRequestsAppointmentsArticles.inContactEnterYour'),
          },
          {
            text: t('help.portalRequestsAppointmentsArticles.inPropertyChooseThe'),
          },
          {
            text: t('help.portalRequestsAppointmentsArticles.inServiceDetailsChoose2'),
          },
          { text: t('help.portalRequestsAppointmentsArticles.inAvailabilityChooseWhen') },
          {
            text: t('help.portalRequestsAppointmentsArticles.inReviewCheckEverything'),
          },
        ],
        whatHappensNext: t('help.portalRequestsAppointmentsArticles.youSeeRequestReceived'),
      },
      {
        slug: 'follow-your-requests',
        title: t('help.portalRequestsAppointmentsArticles.followYourRequests'),
        description: t('help.portalRequestsAppointmentsArticles.checkTheStatusOf'),
        icon: 'pi-list',
        introduction: t('help.portalRequestsAppointmentsArticles.theRequestsPageLists'),
        steps: [
          { text: t('help.portalRequestsAppointmentsArticles.selectRequestsInThe') },
          {
            text: t('help.portalRequestsAppointmentsArticles.reviewTheTableRequest'),
          },
          { text: t('help.portalRequestsAppointmentsArticles.selectViewOnA') },
          {
            text: t('help.portalRequestsAppointmentsArticles.theDetailPageShows'),
          },
          {
            text: t('help.portalRequestsAppointmentsArticles.ifAQuoteWas'),
          },
        ],
        whatHappensNext: t('help.portalRequestsAppointmentsArticles.theStatusChangesAs'),
      },
    ],
  },
  {
    label: t('help.portalRequestsAppointmentsArticles.appointments'),
    articles: [
      {
        slug: 'view-your-appointments',
        title: t('help.portalRequestsAppointmentsArticles.viewYourAppointments'),
        description: t('help.portalRequestsAppointmentsArticles.seeUpcomingAndPast'),
        icon: 'pi-calendar',
        introduction: t('help.portalRequestsAppointmentsArticles.theAppointmentsPageShows'),
        steps: [
          { text: t('help.portalRequestsAppointmentsArticles.selectAppointmentsInThe') },
          { text: t('help.portalRequestsAppointmentsArticles.useTheUpcomingAnd') },
          {
            text: t('help.portalRequestsAppointmentsArticles.reviewTheDateTime'),
          },
          { text: t('help.portalRequestsAppointmentsArticles.selectViewToOpen') },
          {
            text: t('help.portalRequestsAppointmentsArticles.theDetailPageShows2'),
          },
        ],
        whatHappensNext: t('help.portalRequestsAppointmentsArticles.yourNextVisitAlso'),
      },
      {
        slug: 'request-a-reschedule',
        title: t('help.portalRequestsAppointmentsArticles.requestAReschedule'),
        description: t('help.portalRequestsAppointmentsArticles.askForADifferent'),
        icon: 'pi-clock',
        introduction: t('help.portalRequestsAppointmentsArticles.ifAVisitNo'),
        callout: t('help.portalRequestsAppointmentsArticles.yourAppointmentDoesNot'),
        steps: [
          {
            text: t('help.portalRequestsAppointmentsArticles.openTheAppointmentFrom'),
          },
          { text: t('help.portalRequestsAppointmentsArticles.selectRequestReschedule') },
          { text: t('help.portalRequestsAppointmentsArticles.chooseAPreferredDate') },
          { text: t('help.portalRequestsAppointmentsArticles.addAReasonSo') },
          { text: t('help.portalRequestsAppointmentsArticles.selectSendRequestSelect') },
        ],
        whatHappensNext: t('help.portalRequestsAppointmentsArticles.theAppointmentShowsReschedule'),
      },
    ],
  },
]);
