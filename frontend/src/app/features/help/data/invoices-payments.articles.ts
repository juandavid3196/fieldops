import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Invoices & payments articles. Content describes only behavior FieldOps implements (BR-10). */
export const INVOICES_PAYMENTS_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.invoicesPaymentsArticles.invoicing'),
    articles: [
      {
        slug: 'create-an-invoice',
        title: t('help.invoicesPaymentsArticles.createAnInvoice'),
        description: t('help.invoicesPaymentsArticles.generateAnInvoiceFrom'),
        icon: 'pi-file-edit',
        introduction: t('help.invoicesPaymentsArticles.invoicesAreGeneratedFrom'),
        steps: [
          {
            text: t('help.invoicesPaymentsArticles.selectInvoicesInThe'),
          },
          {
            text: t('help.invoicesPaymentsArticles.selectACompletedJob'),
          },
          {
            text: t('help.invoicesPaymentsArticles.reviewTheBillingComparison'),
          },
          {
            text: t('help.invoicesPaymentsArticles.inInvoiceDetailsCheck'),
          },
          { text: t('help.invoicesPaymentsArticles.selectGenerateInvoiceIf') },
        ],
        whatHappensNext: t('help.invoicesPaymentsArticles.generatingCreatesADraft'),
      },
      {
        slug: 'review-completed-jobs-before-invoicing',
        title: t('help.invoicesPaymentsArticles.reviewCompletedJobsBefore'),
        description: t('help.invoicesPaymentsArticles.verifyFinishedWorkAgainst'),
        icon: 'pi-list-check',
        introduction: t('help.invoicesPaymentsArticles.completedJobsWaitIn'),
        callout: t('help.invoicesPaymentsArticles.onlyOwnersAndAccounting'),
        steps: [
          {
            text: t('help.invoicesPaymentsArticles.selectInvoicesInThe2'),
          },
          {
            text: t('help.invoicesPaymentsArticles.inTheQueueUse'),
          },
          {
            text: t('help.invoicesPaymentsArticles.inBillingReviewRead'),
          },
          {
            text: t('help.invoicesPaymentsArticles.openWorkEvidenceTo'),
          },
          {
            text: t('help.invoicesPaymentsArticles.inInvoiceDetailsCheck2'),
          },
          {
            text: t('help.invoicesPaymentsArticles.selectGenerateInvoiceIf2'),
          },
        ],
        whatHappensNext: t('help.invoicesPaymentsArticles.generatingCreatesADraft2'),
      },
      {
        slug: 'review-and-send-an-invoice',
        title: t('help.invoicesPaymentsArticles.reviewAndSendAn'),
        description: t('help.invoicesPaymentsArticles.confirmChargesBeforeSending'),
        icon: 'pi-send',
        introduction: t('help.invoicesPaymentsArticles.reviewADraftInvoice'),
        callout: t('help.invoicesPaymentsArticles.someDeliveryAndPayment'),
        steps: [
          { text: t('help.invoicesPaymentsArticles.selectInvoicesThenDrafts') },
          {
            text: t('help.invoicesPaymentsArticles.reviewTheCustomerLine'),
          },
          {
            text: t('help.invoicesPaymentsArticles.inTheSendPanel'),
          },
          { text: t('help.invoicesPaymentsArticles.selectSendInvoiceAnd') },
        ],
        whatHappensNext: t('help.invoicesPaymentsArticles.theInvoiceMovesTo'),
      },
      {
        slug: 'report-a-bank-transfer',
        title: t('help.invoicesPaymentsArticles.reportABankTransfer'),
        description: t('help.invoicesPaymentsArticles.recordTransferDetailsAwaiting'),
        icon: 'pi-building-columns',
        introduction: t('help.invoicesPaymentsArticles.bankTransfersAreReported'),
        callout: t('help.invoicesPaymentsArticles.reviewingAReportedTransfer'),
        steps: [
          {
            text: t('help.invoicesPaymentsArticles.makeSureYourBank'),
          },
          {
            text: t('help.invoicesPaymentsArticles.sendTheInvoiceThe'),
          },
          {
            text: t('help.invoicesPaymentsArticles.whenTheMoneyArrives'),
          },
        ],
        whatHappensNext: t('help.invoicesPaymentsArticles.recordingThePaymentUpdates'),
      },
    ],
  },
  {
    label: t('help.invoicesPaymentsArticles.payments'),
    articles: [
      {
        slug: 'record-a-payment',
        title: t('help.invoicesPaymentsArticles.recordAPayment'),
        description: t('help.invoicesPaymentsArticles.applyAReceivedPayment'),
        icon: 'pi-credit-card',
        introduction: t('help.invoicesPaymentsArticles.recordMoneyYouAlready'),
        steps: [
          {
            text: t('help.invoicesPaymentsArticles.selectInvoicesAndFind'),
          },
          { text: t('help.invoicesPaymentsArticles.openTheRowMenu') },
          { text: t('help.invoicesPaymentsArticles.enterTheAmountThe') },
          {
            text: t('help.invoicesPaymentsArticles.addTheReferenceNumber'),
          },
          { text: t('help.invoicesPaymentsArticles.chooseWhetherToSend') },
          { text: t('help.invoicesPaymentsArticles.selectRecordExternalPayment') },
        ],
        whatHappensNext: t('help.invoicesPaymentsArticles.fieldopsCreatesAPayment'),
      },
      {
        slug: 'review-overdue-invoices',
        title: t('help.invoicesPaymentsArticles.reviewOverdueInvoices'),
        description: t('help.invoicesPaymentsArticles.findBalancesThatNeed'),
        icon: 'pi-clock',
        introduction: t('help.invoicesPaymentsArticles.overdueInvoicesAreSent'),
        steps: [
          { text: t('help.invoicesPaymentsArticles.selectInvoicesInThe3') },
          {
            text: t('help.invoicesPaymentsArticles.reviewEachInvoicesDue'),
          },
          {
            text: t('help.invoicesPaymentsArticles.onTheInvoicesPage'),
          },
          { text: t('help.invoicesPaymentsArticles.openTheRowMenu2') },
        ],
        whatHappensNext: t('help.invoicesPaymentsArticles.onceAPaymentIs'),
      },
      {
        slug: 'void-an-invoice',
        title: t('help.invoicesPaymentsArticles.voidAnInvoice'),
        description: t('help.invoicesPaymentsArticles.cancelAnInvoiceWithout'),
        icon: 'pi-ban',
        introduction: t('help.invoicesPaymentsArticles.voidingAnInvoiceIs'),
        callout: t('help.invoicesPaymentsArticles.contactSupportToAsk'),
        steps: [
          { text: t('help.invoicesPaymentsArticles.selectInvoicesAndOpen') },
          {
            text: t('help.invoicesPaymentsArticles.noteTheInvoiceNumber'),
          },
          {
            text: t('help.invoicesPaymentsArticles.selectContactSupportAnd'),
          },
        ],
        whatHappensNext: t('help.invoicesPaymentsArticles.theInvoiceAndIts'),
      },
    ],
  },
]);
