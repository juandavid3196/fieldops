import { HelpPopularQuestion, HelpTopic } from '../models/help.model';
import { ACCOUNT_ADMINISTRATION_SECTIONS } from './account-administration.articles';
import { DASHBOARDS_INSIGHTS_SECTIONS } from './dashboards-insights.articles';
import { GETTING_STARTED_SECTIONS } from './getting-started.articles';
import { INVOICES_PAYMENTS_SECTIONS } from './invoices-payments.articles';
import { PORTAL_QUOTES_INVOICES_SECTIONS } from './portal-quotes-invoices.articles';
import { PORTAL_REQUESTS_APPOINTMENTS_SECTIONS } from './portal-requests-appointments.articles';
import { PORTAL_YOUR_PORTAL_SECTIONS } from './portal-your-portal.articles';
import { REQUESTS_QUOTES_JOBS_SECTIONS } from './requests-quotes-jobs.articles';
import { SCHEDULING_TEAM_SECTIONS } from './scheduling-team.articles';
import { TECHNICIAN_FIELD_APP_SECTIONS } from './technician-field-app.articles';

import { localizedList, t } from '../../../core/i18n/translate';
/**
 * BR-03, BR-04: the ten topics in catalog order (7 internal, then 3 portal), with their popular
 * entries (1, 2, 3).
 */
export const HELP_TOPICS: readonly HelpTopic[] = localizedList(() => [
  {
    slug: 'getting-started',
    audience: 'internal',
    title: t('help.helpCatalog.gettingStarted'),
    icon: 'pi-book',
    cardSubtitle: t('help.helpCatalog.setUpYourWorkspace'),
    headerSubtitle: t('help.helpCatalog.setUpYourWorkspace2'),
    sections: GETTING_STARTED_SECTIONS,
    popular: ['add-your-first-customer', 'invite-your-team', 'create-and-manage-branches'],
  },
  {
    slug: 'requests-quotes-jobs',
    audience: 'internal',
    title: t('help.helpCatalog.requestsQuotesJobs'),
    icon: 'pi-file',
    cardSubtitle: t('help.helpCatalog.manageWorkFromThe'),
    headerSubtitle: t('help.helpCatalog.manageCustomerWorkFrom'),
    sections: REQUESTS_QUOTES_JOBS_SECTIONS,
    popular: ['create-a-job-from-a-quote', 'build-and-send-a-quote', 'complete-a-job'],
  },
  {
    slug: 'scheduling-team',
    audience: 'internal',
    title: t('help.helpCatalog.schedulingTeam'),
    icon: 'pi-users',
    cardSubtitle: t('help.helpCatalog.assignVisitsAndCoordinate'),
    headerSubtitle: t('help.helpCatalog.planVisitsAndCoordinate'),
    sections: SCHEDULING_TEAM_SECTIONS,
    popular: ['assign-a-technician', 'schedule-a-visit', 'update-a-visit-status'],
  },
  {
    slug: 'invoices-payments',
    audience: 'internal',
    title: t('help.helpCatalog.invoicesPayments'),
    icon: 'pi-credit-card',
    cardSubtitle: t('help.helpCatalog.createInvoicesAndTrack'),
    headerSubtitle: t('help.helpCatalog.createInvoicesAndKeep'),
    sections: INVOICES_PAYMENTS_SECTIONS,
    popular: ['create-an-invoice', 'record-a-payment', 'review-overdue-invoices'],
  },
  {
    slug: 'technician-field-app',
    audience: 'internal',
    title: t('help.helpCatalog.technicianFieldApp'),
    icon: 'pi-mobile',
    cardSubtitle: t('help.helpCatalog.runYourDayFrom'),
    headerSubtitle: t('help.helpCatalog.workYourVisitsFrom'),
    sections: TECHNICIAN_FIELD_APP_SECTIONS,
    popular: [
      'travel-arrive-and-start-a-job',
      'complete-tasks-and-add-materials',
      'review-and-complete-a-job',
    ],
  },
  {
    slug: 'dashboards-insights',
    audience: 'internal',
    title: t('help.helpCatalog.dashboardsInsights'),
    icon: 'pi-chart-bar',
    cardSubtitle: t('help.helpCatalog.trackPerformanceReportsAnd'),
    headerSubtitle: t('help.helpCatalog.followYourOperationReports'),
    sections: DASHBOARDS_INSIGHTS_SECTIONS,
    popular: [
      'read-the-operations-dashboard',
      'use-the-reports-workspace',
      'read-the-business-overview',
    ],
  },
  {
    slug: 'account-administration',
    audience: 'internal',
    title: t('help.helpCatalog.accountAdministration'),
    icon: 'pi-cog',
    cardSubtitle: t('help.helpCatalog.manageYourAccountUsers'),
    headerSubtitle: t('help.helpCatalog.manageYourOwnAccount'),
    sections: ACCOUNT_ADMINISTRATION_SECTIONS,
    popular: [
      'manage-users-and-permissions',
      'manage-technician-skills-and-availability',
      'update-your-profile',
    ],
  },
  {
    slug: 'portal-your-portal',
    audience: 'portal',
    title: t('help.helpCatalog.yourCustomerPortal'),
    icon: 'pi-home',
    cardSubtitle: t('help.helpCatalog.signInFindYour'),
    headerSubtitle: t('help.helpCatalog.accessYourPortalAnd'),
    sections: PORTAL_YOUR_PORTAL_SECTIONS,
    popular: ['activate-your-portal-access', 'get-around-your-portal', 'manage-your-properties'],
  },
  {
    slug: 'portal-requests-appointments',
    audience: 'portal',
    title: t('help.helpCatalog.requestsAppointments'),
    icon: 'pi-calendar',
    cardSubtitle: t('help.helpCatalog.askForServiceAnd'),
    headerSubtitle: t('help.helpCatalog.requestWorkFollowIts'),
    sections: PORTAL_REQUESTS_APPOINTMENTS_SECTIONS,
    popular: ['request-a-service', 'view-your-appointments', 'request-a-reschedule'],
  },
  {
    slug: 'portal-quotes-invoices',
    audience: 'portal',
    title: t('help.helpCatalog.quotesInvoices'),
    icon: 'pi-wallet',
    cardSubtitle: t('help.helpCatalog.reviewQuotesAndPay'),
    headerSubtitle: t('help.helpCatalog.approveWorkAndPay'),
    sections: PORTAL_QUOTES_INVOICES_SECTIONS,
    popular: ['review-and-approve-a-quote', 'pay-an-invoice-online', 'view-your-invoices'],
  },
]);

/** BR-05: portal popular questions on `/portal/help`, in order. */
export const HELP_PORTAL_POPULAR_QUESTIONS: readonly HelpPopularQuestion[] = localizedList(() => [
  {
    question: t('help.helpCatalog.howDoIRequest'),
    answer: t('help.helpCatalog.onYourPortalHome'),
    topicSlug: 'portal-requests-appointments',
    articleSlug: 'request-a-service',
  },
  {
    question: t('help.helpCatalog.howDoIApprove'),
    answer: t('help.helpCatalog.openTheQuoteFrom'),
    topicSlug: 'portal-quotes-invoices',
    articleSlug: 'review-and-approve-a-quote',
  },
  {
    question: t('help.helpCatalog.howDoIPay'),
    answer: t('help.helpCatalog.openTheInvoiceFrom'),
    topicSlug: 'portal-quotes-invoices',
    articleSlug: 'pay-an-invoice-online',
  },
  {
    question: t('help.helpCatalog.howDoIReschedule'),
    answer: t('help.helpCatalog.openTheAppointmentFrom'),
    topicSlug: 'portal-requests-appointments',
    articleSlug: 'request-a-reschedule',
  },
]);

/** BR-05: popular questions on the home page, in order. */
export const HELP_POPULAR_QUESTIONS: readonly HelpPopularQuestion[] = localizedList(() => [
  {
    question: t('help.helpCatalog.howDoICreate'),
    answer: t('help.helpCatalog.selectRequestsInThe'),
    topicSlug: 'requests-quotes-jobs',
    articleSlug: 'create-a-service-request',
  },
  {
    question: t('help.helpCatalog.howDoITurn'),
    answer: t('help.helpCatalog.openTheApprovedQuote'),
    topicSlug: 'requests-quotes-jobs',
    articleSlug: 'create-a-job-from-a-quote',
  },
  {
    question: t('help.helpCatalog.howDoIAssign'),
    answer: t('help.helpCatalog.openTheVisitFrom'),
    topicSlug: 'scheduling-team',
    articleSlug: 'assign-a-technician',
  },
  {
    question: t('help.helpCatalog.howDoIRecord'),
    answer: t('help.helpCatalog.inInvoicesOpenThe'),
    topicSlug: 'invoices-payments',
    articleSlug: 'record-a-payment',
  },
]);
