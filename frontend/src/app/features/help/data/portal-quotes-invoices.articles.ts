import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Customer help: quotes and invoices. Content describes only behavior FieldOps implements (BR-10). */
export const PORTAL_QUOTES_INVOICES_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.portalQuotesInvoicesArticles.quotes'),
    articles: [
      {
        slug: 'review-and-approve-a-quote',
        title: t('help.portalQuotesInvoicesArticles.reviewAndApproveA'),
        description: t('help.portalQuotesInvoicesArticles.checkThePriceAnd'),
        icon: 'pi-file-edit',
        introduction: t('help.portalQuotesInvoicesArticles.whenAQuoteIs'),
        callout: t('help.portalQuotesInvoicesArticles.approvingAQuoteDoes'),
        steps: [
          {
            text: t('help.portalQuotesInvoicesArticles.openTheQuoteFrom'),
          },
          {
            text: t('help.portalQuotesInvoicesArticles.checkTheQuoteNumber'),
          },
          {
            text: t('help.portalQuotesInvoicesArticles.reviewThePriceDetails'),
          },
          { text: t('help.portalQuotesInvoicesArticles.readTheTermsAnd') },
          {
            text: t('help.portalQuotesInvoicesArticles.selectIApproveThe'),
          },
          { text: t('help.portalQuotesInvoicesArticles.selectDownloadPdfIf') },
        ],
        whatHappensNext: t('help.portalQuotesInvoicesArticles.yourApprovalIsRecorded'),
      },
      {
        slug: 'ask-a-question-or-decline-a-quote',
        title: t('help.portalQuotesInvoicesArticles.askAQuestionOr'),
        description: t('help.portalQuotesInvoicesArticles.getAnswersOrTurn'),
        icon: 'pi-comments',
        introduction: t('help.portalQuotesInvoicesArticles.ifYouAreNot'),
        callout: t('help.portalQuotesInvoicesArticles.decliningCannotBeUndone'),
        steps: [
          { text: t('help.portalQuotesInvoicesArticles.openTheQuoteFrom2') },
          {
            text: t('help.portalQuotesInvoicesArticles.toAskSomethingSelect'),
          },
          {
            text: t('help.portalQuotesInvoicesArticles.toTurnTheQuote'),
          },
          {
            text: t('help.portalQuotesInvoicesArticles.checkTheResultIn'),
          },
        ],
        whatHappensNext: t('help.portalQuotesInvoicesArticles.afterYouSendA'),
      },
    ],
  },
  {
    label: t('help.portalQuotesInvoicesArticles.invoices'),
    articles: [
      {
        slug: 'view-your-invoices',
        title: t('help.portalQuotesInvoicesArticles.viewYourInvoices'),
        description: t('help.portalQuotesInvoicesArticles.checkBalancesDueDates'),
        icon: 'pi-receipt',
        introduction: t('help.portalQuotesInvoicesArticles.invoicesListsEveryInvoice'),
        steps: [
          {
            text: t('help.portalQuotesInvoicesArticles.selectInvoicesInThe'),
          },
          {
            text: t('help.portalQuotesInvoicesArticles.readTheTableInvoice'),
          },
          {
            text: t('help.portalQuotesInvoicesArticles.checkTheStatusSent'),
          },
          { text: t('help.portalQuotesInvoicesArticles.selectViewToOpen') },
        ],
        whatHappensNext: t('help.portalQuotesInvoicesArticles.fromAnOpenInvoice'),
      },
      {
        slug: 'pay-an-invoice-online',
        title: t('help.portalQuotesInvoicesArticles.payAnInvoiceOnline'),
        description: t('help.portalQuotesInvoicesArticles.paySecurelyFromYour'),
        icon: 'pi-credit-card',
        introduction: t('help.portalQuotesInvoicesArticles.openInvoicesIncludeA'),
        callout: t('help.portalQuotesInvoicesArticles.whichMethodsYouCan'),
        steps: [
          { text: t('help.portalQuotesInvoicesArticles.openTheInvoiceFrom') },
          {
            text: t('help.portalQuotesInvoicesArticles.inPayInvoiceChoose'),
          },
          { text: t('help.portalQuotesInvoicesArticles.enterTheCardholderName') },
          {
            text: t('help.portalQuotesInvoicesArticles.selectThePayButton'),
          },
          {
            text: t('help.portalQuotesInvoicesArticles.ifThePaymentFails'),
          },
        ],
        whatHappensNext: t('help.portalQuotesInvoicesArticles.whenThePaymentIs'),
      },
      {
        slug: 'report-a-bank-transfer',
        title: t('help.portalQuotesInvoicesArticles.reportABankTransfer'),
        description: t('help.portalQuotesInvoicesArticles.tellUsYouSent'),
        icon: 'pi-send',
        introduction: t('help.portalQuotesInvoicesArticles.ifYouPayBy'),
        callout: t('help.portalQuotesInvoicesArticles.reportingATransferDoes'),
        steps: [
          { text: t('help.portalQuotesInvoicesArticles.openTheInvoiceAnd') },
          {
            text: t('help.portalQuotesInvoicesArticles.useTheBankTransfer'),
          },
          { text: t('help.portalQuotesInvoicesArticles.useTheInvoiceNumber') },
          { text: t('help.portalQuotesInvoicesArticles.sendTheTransferFrom') },
          {
            text: t('help.portalQuotesInvoicesArticles.ifYouHaveQuestions'),
          },
        ],
        whatHappensNext: t('help.portalQuotesInvoicesArticles.theInvoiceShowsTransfer'),
      },
    ],
  },
]);
