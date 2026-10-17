import { HelpSection } from '../models/help.model';

/** Customer help: quotes and invoices. Content describes only behavior FieldOps implements (BR-10). */
export const PORTAL_QUOTES_INVOICES_SECTIONS: readonly HelpSection[] = [
  {
    label: 'QUOTES',
    articles: [
      {
        slug: 'review-and-approve-a-quote',
        title: 'Review and approve a quote',
        description: 'Check the price and approve the work.',
        icon: 'pi-file-edit',
        introduction:
          'When a quote is ready, you can review the proposed work and price and approve it online. Open it from the link in your email, or from Quotes in the portal menu.',
        callout:
          'Approving a quote does not charge your payment method. A quote can only be approved until its Valid until date.',
        steps: [
          {
            text: 'Open the quote from your email, or select Quotes in the portal and choose Review next to a quote marked Awaiting your approval. The Home page also shows it under Action required.',
          },
          {
            text: 'Check the quote number, the Sent date, the Valid until date and the Quote total. The total shows No payment due today.',
          },
          {
            text: 'Review the Price details. If some items are optional, use their toggles to include or leave them out; the total updates.',
          },
          { text: 'Read the Terms and next steps section.' },
          {
            text: 'Select I approve the scope of work and agree to the terms, then select Approve quote.',
          },
          { text: 'Select Download PDF if you want a copy of the quote.' },
        ],
        whatHappensNext:
          'Your approval is recorded and the quote shows as Approved. The company then contacts you to confirm the service date and assigned technician.',
      },
      {
        slug: 'ask-a-question-or-decline-a-quote',
        title: 'Ask a question or decline a quote',
        description: 'Get answers or turn a quote down.',
        icon: 'pi-comments',
        introduction:
          'If you are not ready to approve, you can send the company a question or decline the quote. Both options are on the quote page.',
        callout:
          'Declining cannot be undone. A quote that has expired or was replaced by a newer one can no longer be answered; contact the company if you still want the work.',
        steps: [
          { text: 'Open the quote from your email or from Quotes in the portal.' },
          {
            text: 'To ask something, select Ask a question, write your Question and select Send question. The company will contact you soon. You can send one question per quote.',
          },
          {
            text: 'To turn the quote down, select Decline quote, write a Reason in the Decline this quote? window and select Decline quote again to confirm.',
          },
          {
            text: 'Check the result in Quotes: the status changes to Question sent or Declined.',
          },
        ],
        whatHappensNext:
          'After you send a question, the quote stays open so you can still approve it once you have your answer. After you decline, your decision is sent to the company and the quote is closed.',
      },
    ],
  },
  {
    label: 'INVOICES',
    articles: [
      {
        slug: 'view-your-invoices',
        title: 'View your invoices',
        description: 'Check balances, due dates and status.',
        icon: 'pi-receipt',
        introduction:
          'Invoices lists every invoice the company has sent you, with what you still owe and when it is due.',
        steps: [
          {
            text: 'Select Invoices in the portal menu. Invoices that need payment also appear under Payment due on Home, and the company emails you a link when an invoice is sent.',
          },
          {
            text: 'Read the table: Invoice, Service, Issued, Due, Total, Balance due and Status.',
          },
          {
            text: 'Check the status: Sent, Partially paid, Overdue or Paid.',
          },
          { text: 'Select View to open an invoice and see its full details.' },
        ],
        whatHappensNext:
          'From an open invoice you can pay it or report a bank transfer. Once it is fully paid, the invoice shows as Paid.',
      },
      {
        slug: 'pay-an-invoice-online',
        title: 'Pay an invoice online',
        description: 'Pay securely from your invoice.',
        icon: 'pi-credit-card',
        introduction:
          'Open invoices include a Pay invoice panel where you choose how to pay. Card payments are processed through Stripe, and your card details are entered in its secure card form.',
        callout:
          'Which methods you can use depends on what the company has enabled. A method that is not available is disabled, with a short reason shown below the options.',
        steps: [
          { text: 'Open the invoice from Invoices in the portal or from the emailed link.' },
          {
            text: 'In Pay invoice, choose Credit or debit card (selected by default when available).',
          },
          { text: 'Enter the Cardholder name and your card details in the secure card form.' },
          {
            text: 'Select the Pay button, which shows the amount you owe, and keep the page open while your payment is processed.',
          },
          {
            text: 'If the payment fails, select Try payment again, or Choose another payment method.',
          },
        ],
        whatHappensNext:
          'When the payment is confirmed you see Payment successful with your receipt number, and you can select Download receipt or Download invoice. If the invoice has a receipt email on file, the page says a receipt was sent to that address.',
      },
      {
        slug: 'report-a-bank-transfer',
        title: 'Report a bank transfer',
        description: 'Tell us you sent a transfer.',
        icon: 'pi-send',
        introduction:
          'If you pay by bank transfer, send the money from your bank and then tell the company so they can look for it.',
        callout:
          'Reporting a transfer does not mark the invoice as paid. Bank transfer must be enabled by the company; otherwise it is disabled. Bank transfers may take 1–3 business days to appear. For cash, the invoice shows how to contact the company to arrange it.',
        steps: [
          { text: 'Open the invoice and, in Pay invoice, choose Bank transfer (ACH).' },
          {
            text: 'Use the Bank transfer instructions. Select the copy button next to a detail to copy it.',
          },
          { text: 'Use the invoice number as the payment reference.' },
          { text: 'Send the transfer from your bank, then select I sent the transfer.' },
          {
            text: 'If you have questions, select I have a question about this invoice when it is shown.',
          },
        ],
        whatHappensNext:
          'The invoice shows Transfer reported on the date you selected the button. The company confirms it once it arrives; until then the invoice stays open.',
      },
    ],
  },
];
