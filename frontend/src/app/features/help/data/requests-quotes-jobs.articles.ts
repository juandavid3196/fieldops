import { HelpSection } from '../models/help.model';

/** Requests, quotes & jobs articles. Content describes only behavior FieldOps implements (BR-10). */
export const REQUESTS_QUOTES_JOBS_SECTIONS: readonly HelpSection[] = [
  {
    label: 'REQUESTS & QUOTES',
    articles: [
      {
        slug: 'create-a-service-request',
        title: 'Create a service request',
        description: 'Record a new customer request and its details.',
        icon: 'pi-file',
        introduction:
          'A request records what a customer needs. Create one yourself when a customer calls or writes, or review the ones that arrive through your public request form.',
        callout:
          'The customer and property must already exist. Add them first from Customers if needed.',
        steps: [
          { text: 'Select Requests in the sidebar, then New request.' },
          {
            text: 'Search and choose the Customer, then choose the Contact and the Property.',
          },
          { text: 'Choose the Category and the Service, and describe the work in Description.' },
          {
            text: 'Set the Priority and the Availability. Add a preferred date and time window if the customer gave one.',
          },
          { text: 'Add any scheduling notes and select Create request.' },
        ],
        whatHappensNext:
          'The request appears in the New requests column of the board. Open it to schedule an assessment, request information, mark it ready for quote, assign it or change its priority.',
      },
      {
        slug: 'build-and-send-a-quote',
        title: 'Build and send a quote',
        description: 'Add services, pricing and send it for approval.',
        icon: 'pi-dollar',
        introduction:
          'Quotes are created from a request that is ready for quote. You add the priced work, review the totals and send the quote to the customer by email.',
        callout: 'SMS delivery is not available yet. Quotes are sent by email.',
        steps: [
          {
            text: 'Open the request in Requests. When it is in Ready for quote, select Create quote.',
          },
          {
            text: 'Add lines with Add service, Add material or Add optional item. Pick items from the catalog and adjust quantity, rate and tax.',
          },
          { text: 'Use Add discount if needed and review the Quote summary totals.' },
          {
            text: 'Write the Customer message, add an Internal note if you want one, and review the Terms and Quote expiration.',
          },
          { text: 'Select Save draft to keep working later.' },
          {
            text: 'In Customer approval, check the recipient email and the message, then select Send quote and confirm.',
          },
        ],
        whatHappensNext:
          'The customer receives a secure link to review and respond to the quote. Follow its status in Quotes. After it is sent you can use Revise quote or Resend email from the quote page.',
      },
      {
        slug: 'record-a-quote-response',
        title: 'Record a quote response',
        description: 'Capture an approval, rejection or clarification.',
        icon: 'pi-comments',
        introduction:
          'Customers answer a quote themselves from the secure link they receive: they can approve it, decline it or ask a question. FieldOps records that answer on the quote.',
        callout:
          'Staff cannot record a response on behalf of a customer in the app yet. Contact support if you need help with a response you received outside FieldOps.',
        steps: [
          { text: 'Select Quotes in the sidebar and open the quote.' },
          {
            text: 'Find the Customer response section. Each response shows its type (Approved, Declined or Question), who sent it, when, and any comment.',
          },
          {
            text: 'If the customer asked a question or declined, use Revise quote to prepare a new version and send it again.',
          },
          {
            text: 'If the customer did not receive the email, use Resend email. The previous link stops working.',
          },
        ],
        whatHappensNext:
          'An approved quote can be turned into a job with Create work order. A declined or questioned quote can be revised and sent again.',
      },
      {
        slug: 'schedule-an-assessment',
        title: 'Schedule an assessment',
        description: 'Visit the site before preparing a quote.',
        icon: 'pi-calendar-clock',
        introduction:
          'An assessment is a site visit to diagnose the work before you quote it. It is optional: if the request already has enough information, choose Mark ready for quote instead.',
        callout:
          'SMS notifications are not available yet. The customer can only be notified by email, and only when the request has an email address.',
        steps: [
          { text: 'Select Requests in the sidebar and open the request.' },
          {
            text: 'In the request panel (available for New or Needs review requests), select Schedule assessment.',
          },
          {
            text: 'In Find a time, review the customer availability, select a technician and check their calendar. Switch between Week, Day and Agenda views.',
          },
          {
            text: 'In Assessment visit, set the Date, Arrival window, Estimated duration and Assigned technician. Choose a Branch first if asked.',
          },
          {
            text: 'Describe the Purpose for assessment. Add Internal instructions if needed; the customer does not see them.',
          },
          {
            text: 'Review the Customer message preview, keep Email on under Notify customer if you want to tell the customer, then select Schedule assessment.',
          },
        ],
        whatHappensNext:
          'The assessment appears in the request panel. When the visit is done, select Complete assessment to record the Diagnosis, the Recommended scope and optional photos, then continue to the quote. To change the visit, use Reschedule in the request panel, or Cancel assessment in the more-actions menu of the request.',
      },
    ],
  },
  {
    label: 'JOBS & VISITS',
    articles: [
      {
        slug: 'create-a-job-from-a-quote',
        title: 'Create a job from a quote',
        description: 'Turn approved work into an active job.',
        icon: 'pi-wrench',
        introduction:
          'When a customer approves a quote, you turn the approved scope into a job. While you prepare it, FieldOps calls it a work order.',
        steps: [
          { text: 'Select Quotes and open the approved quote.' },
          { text: 'Select Create work order.' },
          {
            text: 'Review the Job details: work order title, job type, service category, branch, priority, estimated duration and skills required.',
          },
          {
            text: 'Add instructions for the assigned technician, the access note, and the tasks and checklist. Use Use checklist template to add saved tasks.',
          },
          {
            text: 'Add the required materials, and set the preferred date, arrival window and recurrence if the work repeats.',
          },
          { text: 'Select Create work order.' },
        ],
        whatHappensNext:
          'The job appears in Jobs, ready to schedule, and stays linked to the approved quote. Pricing remains tied to that quote. The next step is to schedule a visit.',
      },
      {
        slug: 'add-the-first-visit',
        title: 'Add the first visit',
        description: 'Choose the date, time and work location.',
        icon: 'pi-calendar',
        introduction:
          'Visits are created together with the job and then scheduled in the Schedule area. There is no separate control to add a visit by hand.',
        callout:
          'Extra visits come from the Recurrence option of the work order. If you need another visit that is not covered, contact support.',
        steps: [
          { text: 'Select Jobs in the sidebar and find the job that needs a visit.' },
          {
            text: 'Open the row menu and select Schedule visit (shown when the job has a visit to schedule). The Schedule opens with that visit selected.',
          },
          {
            text: 'In the Assign work order panel, choose the Date, Start time, End time and, if needed, the Preferred arrival window.',
          },
          {
            text: 'The times use the branch time zone. Select Schedule, or Assign if you also chose technicians (the button adds & notify when the customer will be notified).',
          },
        ],
        whatHappensNext:
          'The visit appears on the dispatch calendar and the job shows it under Visits. Assign a technician to complete the plan.',
      },
      {
        slug: 'complete-a-job',
        title: 'Complete a job',
        description: 'Review finished work and prepare it for billing.',
        icon: 'pi-check-circle',
        introduction:
          "The assigned technician completes the visit from Today's jobs. The office then reviews the finished work before invoicing.",
        steps: [
          {
            text: "The technician opens the visit from Today's jobs, selects Start travel, then I've arrived, then Start job.",
          },
          {
            text: 'While working, the technician completes the checklist, records materials and adds before and after photos and notes.',
          },
          {
            text: 'The technician selects Review & complete, checks the summary and selects Continue to acknowledgment.',
          },
          {
            text: 'The technician records how the customer acknowledged the work (for example a signature) and selects Complete job.',
          },
          {
            text: 'In the office, open Jobs and use Review completion in the row menu of the completed job.',
          },
        ],
        whatHappensNext:
          'The job opens in Invoices under Needs review, where you compare approved work with the actual work and create the invoice.',
      },
    ],
  },
];
