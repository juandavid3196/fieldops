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

/**
 * BR-03, BR-04: the ten topics in catalog order (7 internal, then 3 portal), with their popular
 * entries (1, 2, 3).
 */
export const HELP_TOPICS: readonly HelpTopic[] = [
  {
    slug: 'getting-started',
    audience: 'internal',
    title: 'Getting started',
    icon: 'pi-book',
    cardSubtitle: 'Set up your workspace and learn the basics.',
    headerSubtitle: 'Set up your workspace and learn the FieldOps basics.',
    sections: GETTING_STARTED_SECTIONS,
    popular: ['add-your-first-customer', 'invite-your-team', 'create-and-manage-branches'],
  },
  {
    slug: 'requests-quotes-jobs',
    audience: 'internal',
    title: 'Requests, quotes & jobs',
    icon: 'pi-file',
    cardSubtitle: 'Manage work from the first request to completion.',
    headerSubtitle: 'Manage customer work from the first request to completion.',
    sections: REQUESTS_QUOTES_JOBS_SECTIONS,
    popular: ['create-a-job-from-a-quote', 'build-and-send-a-quote', 'complete-a-job'],
  },
  {
    slug: 'scheduling-team',
    audience: 'internal',
    title: 'Scheduling & team',
    icon: 'pi-users',
    cardSubtitle: 'Assign visits and coordinate your field team.',
    headerSubtitle: 'Plan visits and coordinate your field team.',
    sections: SCHEDULING_TEAM_SECTIONS,
    popular: ['assign-a-technician', 'schedule-a-visit', 'update-a-visit-status'],
  },
  {
    slug: 'invoices-payments',
    audience: 'internal',
    title: 'Invoices & payments',
    icon: 'pi-credit-card',
    cardSubtitle: 'Create invoices and track customer payments.',
    headerSubtitle: 'Create invoices and keep customer balances up to date.',
    sections: INVOICES_PAYMENTS_SECTIONS,
    popular: ['create-an-invoice', 'record-a-payment', 'review-overdue-invoices'],
  },
  {
    slug: 'technician-field-app',
    audience: 'internal',
    title: 'Technician field app',
    icon: 'pi-mobile',
    cardSubtitle: "Run your day from the Today's jobs screen.",
    headerSubtitle: 'Work your visits from travel to completion in the field.',
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
    title: 'Dashboards & insights',
    icon: 'pi-chart-bar',
    cardSubtitle: 'Track performance, reports and activity.',
    headerSubtitle: 'Follow your operation, reports and activity across the organization.',
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
    title: 'Account & administration',
    icon: 'pi-cog',
    cardSubtitle: 'Manage your account, users and records.',
    headerSubtitle: 'Manage your own account and the people and records of your organization.',
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
    title: 'Your customer portal',
    icon: 'pi-home',
    cardSubtitle: 'Sign in, find your way and manage your details.',
    headerSubtitle: 'Access your portal and keep your service locations up to date.',
    sections: PORTAL_YOUR_PORTAL_SECTIONS,
    popular: ['activate-your-portal-access', 'get-around-your-portal', 'manage-your-properties'],
  },
  {
    slug: 'portal-requests-appointments',
    audience: 'portal',
    title: 'Requests & appointments',
    icon: 'pi-calendar',
    cardSubtitle: 'Ask for service and track your visits.',
    headerSubtitle: 'Request work, follow its progress and manage your appointments.',
    sections: PORTAL_REQUESTS_APPOINTMENTS_SECTIONS,
    popular: ['request-a-service', 'view-your-appointments', 'request-a-reschedule'],
  },
  {
    slug: 'portal-quotes-invoices',
    audience: 'portal',
    title: 'Quotes & invoices',
    icon: 'pi-wallet',
    cardSubtitle: 'Review quotes and pay your invoices.',
    headerSubtitle: 'Approve work and pay what you owe.',
    sections: PORTAL_QUOTES_INVOICES_SECTIONS,
    popular: ['review-and-approve-a-quote', 'pay-an-invoice-online', 'view-your-invoices'],
  },
];

/** BR-05: portal popular questions on `/portal/help`, in order. */
export const HELP_PORTAL_POPULAR_QUESTIONS: readonly HelpPopularQuestion[] = [
  {
    question: 'How do I request a service?',
    answer:
      'On your portal Home, select Request a service, describe the work you need and send the request. You can follow its progress under Requests.',
    topicSlug: 'portal-requests-appointments',
    articleSlug: 'request-a-service',
  },
  {
    question: 'How do I approve a quote?',
    answer:
      'Open the quote from Quotes or select Review quote on your Home, check the price and select Approve quote.',
    topicSlug: 'portal-quotes-invoices',
    articleSlug: 'review-and-approve-a-quote',
  },
  {
    question: 'How do I pay an invoice?',
    answer:
      'Open the invoice from Invoices and choose to pay online, then pay securely with your preferred payment method.',
    topicSlug: 'portal-quotes-invoices',
    articleSlug: 'pay-an-invoice-online',
  },
  {
    question: 'How do I reschedule an appointment?',
    answer:
      'Open the appointment from Appointments, select Request reschedule and tell us the date or time you prefer.',
    topicSlug: 'portal-requests-appointments',
    articleSlug: 'request-a-reschedule',
  },
];

/** BR-05: popular questions on the home page, in order. */
export const HELP_POPULAR_QUESTIONS: readonly HelpPopularQuestion[] = [
  {
    question: 'How do I create a service request?',
    answer:
      'Select Requests in the sidebar, then New request, choose the customer and property, describe the work and select Create request.',
    topicSlug: 'requests-quotes-jobs',
    articleSlug: 'create-a-service-request',
  },
  {
    question: 'How do I turn an approved quote into a job?',
    answer:
      'Open the approved quote in Quotes and select Create work order. Review the job details and select Create work order again to save it.',
    topicSlug: 'requests-quotes-jobs',
    articleSlug: 'create-a-job-from-a-quote',
  },
  {
    question: 'How do I assign a technician?',
    answer:
      'Open the visit from the Schedule, choose the technicians under Assign to technicians, mark one as primary and select Assign.',
    topicSlug: 'scheduling-team',
    articleSlug: 'assign-a-technician',
  },
  {
    question: 'How do I record a payment?',
    answer:
      'In Invoices, open the row menu of the invoice and select Record payment. Enter the amount, date and method, then select Record external payment.',
    topicSlug: 'invoices-payments',
    articleSlug: 'record-a-payment',
  },
];
