import { HelpSection } from '../models/help.model';

/** Invoices & payments articles. Content describes only behavior FieldOps implements (BR-10). */
export const INVOICES_PAYMENTS_SECTIONS: readonly HelpSection[] = [
  {
    label: 'INVOICING',
    articles: [
      {
        slug: 'create-an-invoice',
        title: 'Create an invoice',
        description: 'Generate an invoice from completed work.',
        icon: 'pi-file-edit',
        introduction:
          'Invoices are generated from completed jobs after you review the work. FieldOps compares the approved quote with the actual work so you can bill with confidence.',
        steps: [
          {
            text: 'Select Invoices in the sidebar, then Needs review. You can also select New invoice on the Invoices page.',
          },
          {
            text: 'Select a completed job from the list. Use Review and invoice from a table row.',
          },
          {
            text: 'Review the Billing comparison, the work evidence and any variances. Add an Accounting note if you need one.',
          },
          {
            text: 'In Invoice details, check the issue date, payment terms, due date, currency and delivery options.',
          },
          { text: 'Select Generate invoice. If there are variances, confirm Generate draft.' },
        ],
        whatHappensNext:
          'Generating creates a draft invoice. Review it before sending. You can find it in Invoices under Drafts.',
      },
      {
        slug: 'review-completed-jobs-before-invoicing',
        title: 'Review completed jobs before invoicing',
        description: 'Verify finished work against the approved quote.',
        icon: 'pi-list-check',
        introduction:
          'Completed jobs wait in a review queue before they are invoiced. Compare the approved quote with the actual work, then generate a draft invoice.',
        callout:
          'Only owners and accounting can review and generate invoices; other roles see the page read-only. Add adjustment, delivery options and online payment options are shown but not available yet.',
        steps: [
          {
            text: 'Select Invoices in the sidebar, then Needs review. The Invoices page also lists jobs under Completed jobs ready to invoice.',
          },
          {
            text: 'In the queue, use the All, Variances and Ready views, then select a job.',
          },
          {
            text: 'In Billing review, read the Billing comparison. Each line shows a status: Matches quote, Over quote, Under quote, Not in quote or Not tracked. Check the Totals comparison and the Completion verification.',
          },
          {
            text: 'Open Work evidence to see the visits and photos, and Audit trail for the history. Add an Accounting note for internal use.',
          },
          {
            text: 'In Invoice details, check the Issue date and Payment terms. The Invoice number, Due date, Tax jurisdiction and Currency are shown for reference.',
          },
          {
            text: 'Select Generate invoice. If the job has variances, confirm Generate draft. Select Return to quote if the job should not be invoiced yet.',
          },
        ],
        whatHappensNext:
          'Generating creates a draft invoice. A confirmation offers View invoice. Variances stay for review only, and the draft uses the approved quote amounts.',
      },
      {
        slug: 'review-and-send-an-invoice',
        title: 'Review and send an invoice',
        description: 'Confirm charges before sending it to the customer.',
        icon: 'pi-send',
        introduction:
          'Review a draft invoice before the customer sees it. Sending delivers the invoice by email.',
        callout:
          'Some delivery and payment options are shown as not available yet, such as SMS notification, online payment methods and attachments.',
        steps: [
          { text: 'Select Invoices, then Drafts, and open the draft invoice.' },
          {
            text: 'Review the customer, line items, totals, issue date and due date. Use Download PDF to see the document.',
          },
          {
            text: 'In the send panel, check the Recipient email, the Payment terms and the Message to customer.',
          },
          { text: 'Select Send invoice and confirm.' },
        ],
        whatHappensNext:
          'The invoice moves to Sent and the customer receives it by email. You can follow its status and payments from Invoices.',
      },
      {
        slug: 'report-a-bank-transfer',
        title: 'Report a bank transfer',
        description: 'Record transfer details awaiting review.',
        icon: 'pi-building-columns',
        introduction:
          'Bank transfers are reported by the customer from their invoice page. Staff do not have a button to report a transfer on their behalf.',
        callout:
          'Reviewing a reported transfer is not available in the app yet. Contact support if you need help with one.',
        steps: [
          {
            text: 'Make sure your bank transfer details are saved: select Administration and open Bank transfer details in Company setup.',
          },
          {
            text: "Send the invoice. The customer's invoice page shows your Bank transfer instructions and lets them report the transfer.",
          },
          {
            text: 'When the money arrives in your account, record it with Record external payment, choosing Bank transfer as the payment method and entering the reference number.',
          },
        ],
        whatHappensNext:
          'Recording the payment updates the invoice balance. See Record a payment for the full steps.',
      },
    ],
  },
  {
    label: 'PAYMENTS',
    articles: [
      {
        slug: 'record-a-payment',
        title: 'Record a payment',
        description: 'Apply a received payment to an invoice.',
        icon: 'pi-credit-card',
        introduction:
          'Record money you already received outside FieldOps against an invoice. FieldOps does not charge any card when you do this.',
        steps: [
          {
            text: 'Select Invoices and find the invoice. Use the search and filters if needed.',
          },
          { text: 'Open the row menu and select Record payment.' },
          { text: 'Enter the Amount, the Payment date and the Payment method.' },
          {
            text: 'Add the Reference number (required for external card payments, bank transfers and checks) and who it was Received by.',
          },
          { text: 'Choose whether to Send receipt, and add an Internal note if you want one.' },
          { text: 'Select Record external payment.' },
        ],
        whatHappensNext:
          'FieldOps creates a payment record and updates the invoice balance. The payment appears under Payments in Invoices.',
      },
      {
        slug: 'review-overdue-invoices',
        title: 'Review overdue invoices',
        description: 'Find balances that need follow-up.',
        icon: 'pi-clock',
        introduction:
          'Overdue invoices are sent invoices with a balance past their due date. Review them regularly to follow up with customers.',
        steps: [
          { text: 'Select Invoices in the sidebar, then Overdue.' },
          {
            text: "Review each invoice's due date, balance and last activity. Use the search and filters to narrow the list.",
          },
          {
            text: 'On the Invoices page, check the Aging summary to see balances grouped as Current, 1 – 30 days, 31 – 60 days and 60+ days.',
          },
          { text: 'Open the row menu and select View invoice to see the details.' },
        ],
        whatHappensNext:
          'Once a payment is recorded, the invoice balance updates and it leaves the overdue list when it is paid.',
      },
      {
        slug: 'void-an-invoice',
        title: 'Void an invoice',
        description: 'Cancel an invoice without deleting its history.',
        icon: 'pi-ban',
        introduction:
          'Voiding an invoice is not available in the app yet. Invoices keep their full history and are not deleted.',
        callout: 'Contact support to ask for an invoice to be cancelled.',
        steps: [
          { text: 'Select Invoices and open the invoice to confirm it is the right one.' },
          {
            text: 'Note the invoice number and the reason you need it cancelled.',
          },
          {
            text: 'Select Contact support and send us the invoice number and the reason.',
          },
        ],
        whatHappensNext:
          'The invoice and its history stay unchanged until support handles your request.',
      },
    ],
  },
];
