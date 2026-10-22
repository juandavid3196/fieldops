import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Requests, quotes & jobs articles. Content describes only behavior FieldOps implements (BR-10). */
export const REQUESTS_QUOTES_JOBS_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.requestsQuotesJobsArticles.requestsQuotes'),
    articles: [
      {
        slug: 'create-a-service-request',
        title: t('help.requestsQuotesJobsArticles.createAServiceRequest'),
        description: t('help.requestsQuotesJobsArticles.recordANewCustomer'),
        icon: 'pi-file',
        introduction: t('help.requestsQuotesJobsArticles.aRequestRecordsWhat'),
        callout: t('help.requestsQuotesJobsArticles.theCustomerAndProperty'),
        steps: [
          { text: t('help.requestsQuotesJobsArticles.selectRequestsInThe') },
          {
            text: t('help.requestsQuotesJobsArticles.searchAndChooseThe'),
          },
          { text: t('help.requestsQuotesJobsArticles.chooseTheCategoryAnd') },
          {
            text: t('help.requestsQuotesJobsArticles.setThePriorityAnd'),
          },
          { text: t('help.requestsQuotesJobsArticles.addAnySchedulingNotes') },
        ],
        whatHappensNext: t('help.requestsQuotesJobsArticles.theRequestAppearsIn'),
      },
      {
        slug: 'build-and-send-a-quote',
        title: t('help.requestsQuotesJobsArticles.buildAndSendA'),
        description: t('help.requestsQuotesJobsArticles.addServicesPricingAnd'),
        icon: 'pi-dollar',
        introduction: t('help.requestsQuotesJobsArticles.quotesAreCreatedFrom'),
        callout: t('help.requestsQuotesJobsArticles.smsDeliveryIsNot'),
        steps: [
          {
            text: t('help.requestsQuotesJobsArticles.openTheRequestIn'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.addLinesWithAdd'),
          },
          { text: t('help.requestsQuotesJobsArticles.useAddDiscountIf') },
          {
            text: t('help.requestsQuotesJobsArticles.writeTheCustomerMessage'),
          },
          { text: t('help.requestsQuotesJobsArticles.selectSaveDraftTo') },
          {
            text: t('help.requestsQuotesJobsArticles.inCustomerApprovalCheck'),
          },
        ],
        whatHappensNext: t('help.requestsQuotesJobsArticles.theCustomerReceivesA'),
      },
      {
        slug: 'record-a-quote-response',
        title: t('help.requestsQuotesJobsArticles.recordAQuoteResponse'),
        description: t('help.requestsQuotesJobsArticles.captureAnApprovalRejection'),
        icon: 'pi-comments',
        introduction: t('help.requestsQuotesJobsArticles.customersAnswerAQuote'),
        callout: t('help.requestsQuotesJobsArticles.staffCannotRecordA'),
        steps: [
          { text: t('help.requestsQuotesJobsArticles.selectQuotesInThe') },
          {
            text: t('help.requestsQuotesJobsArticles.findTheCustomerResponse'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.ifTheCustomerAsked'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.ifTheCustomerDid'),
          },
        ],
        whatHappensNext: t('help.requestsQuotesJobsArticles.anApprovedQuoteCan'),
      },
      {
        slug: 'schedule-an-assessment',
        title: t('help.requestsQuotesJobsArticles.scheduleAnAssessment'),
        description: t('help.requestsQuotesJobsArticles.visitTheSiteBefore'),
        icon: 'pi-calendar-clock',
        introduction: t('help.requestsQuotesJobsArticles.anAssessmentIsA'),
        callout: t('help.requestsQuotesJobsArticles.smsNotificationsAreNot'),
        steps: [
          { text: t('help.requestsQuotesJobsArticles.selectRequestsInThe2') },
          {
            text: t('help.requestsQuotesJobsArticles.inTheRequestPanel'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.inFindATime'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.inAssessmentVisitSet'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.describeThePurposeFor'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.reviewTheCustomerMessage'),
          },
        ],
        whatHappensNext: t('help.requestsQuotesJobsArticles.theAssessmentAppearsIn'),
      },
    ],
  },
  {
    label: t('help.requestsQuotesJobsArticles.jobsVisits'),
    articles: [
      {
        slug: 'create-a-job-from-a-quote',
        title: t('help.requestsQuotesJobsArticles.createAJobFrom'),
        description: t('help.requestsQuotesJobsArticles.turnApprovedWorkInto'),
        icon: 'pi-wrench',
        introduction: t('help.requestsQuotesJobsArticles.whenACustomerApproves'),
        steps: [
          { text: t('help.requestsQuotesJobsArticles.selectQuotesAndOpen') },
          { text: t('help.requestsQuotesJobsArticles.selectCreateWorkOrder') },
          {
            text: t('help.requestsQuotesJobsArticles.reviewTheJobDetails'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.addInstructionsForThe'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.addTheRequiredMaterials'),
          },
          { text: t('help.requestsQuotesJobsArticles.selectCreateWorkOrder') },
        ],
        whatHappensNext: t('help.requestsQuotesJobsArticles.theJobAppearsIn'),
      },
      {
        slug: 'add-the-first-visit',
        title: t('help.requestsQuotesJobsArticles.addTheFirstVisit'),
        description: t('help.requestsQuotesJobsArticles.chooseTheDateTime'),
        icon: 'pi-calendar',
        introduction: t('help.requestsQuotesJobsArticles.visitsAreCreatedTogether'),
        callout: t('help.requestsQuotesJobsArticles.extraVisitsComeFrom'),
        steps: [
          { text: t('help.requestsQuotesJobsArticles.selectJobsInThe') },
          {
            text: t('help.requestsQuotesJobsArticles.openTheRowMenu'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.inTheAssignWork'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.theTimesUseThe'),
          },
        ],
        whatHappensNext: t('help.requestsQuotesJobsArticles.theVisitAppearsOn'),
      },
      {
        slug: 'complete-a-job',
        title: t('help.requestsQuotesJobsArticles.completeAJob'),
        description: t('help.requestsQuotesJobsArticles.reviewFinishedWorkAnd'),
        icon: 'pi-check-circle',
        introduction: t('help.requestsQuotesJobsArticles.theAssignedTechnicianCompletes'),
        steps: [
          {
            text: t('help.requestsQuotesJobsArticles.theTechnicianOpensThe'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.whileWorkingTheTechnician'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.theTechnicianSelectsReview'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.theTechnicianRecordsHow'),
          },
          {
            text: t('help.requestsQuotesJobsArticles.inTheOfficeOpen'),
          },
        ],
        whatHappensNext: t('help.requestsQuotesJobsArticles.theJobOpensIn'),
      },
    ],
  },
]);
