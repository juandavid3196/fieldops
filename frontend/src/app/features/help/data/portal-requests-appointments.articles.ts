import { HelpSection } from '../models/help.model';

/** Customer portal articles: requests and appointments. Content describes only behavior FieldOps implements (BR-10, BR-14). */
export const PORTAL_REQUESTS_APPOINTMENTS_SECTIONS: readonly HelpSection[] = [
  {
    label: 'REQUESTS',
    articles: [
      {
        slug: 'request-a-service',
        title: 'Request a service',
        description: 'Tell us what you need and when.',
        icon: 'pi-plus-circle',
        introduction:
          'Send a service request from your portal in four short steps. Your contact details are already known, so there is no contact form.',
        callout:
          'Submitting a request does not confirm a price or an appointment. Your service company reviews it first. If online requests are turned off, the page tells you to call the company instead.',
        steps: [
          { text: 'Select Request a service on Home or on the Requests page.' },
          {
            text: 'In Property, choose one of your locations or select Add a new property and enter its address.',
          },
          {
            text: 'In Service details, choose the type of service, pick a Service or select the not-sure option, and describe the problem. Choose how urgent it is: Standard, Urgent or Emergency. You can add photos or documents (JPG, PNG or PDF, up to 10 MB each).',
          },
          {
            text: "In Availability, choose As soon as possible, Choose a date or I'm flexible, then a time: Morning, Afternoon, Evening or Any time. Add scheduling notes if you wish.",
          },
          {
            text: 'In Review, check your answers (select Edit to change any), confirm they are accurate and select Submit request.',
          },
        ],
        whatHappensNext:
          'You see Your request is on its way with your request number. Your service company reviews the details and contacts you with the next steps, and changes appear in your updates.',
      },
      {
        slug: 'request-a-service-without-signing-in',
        title: 'Request a service without signing in',
        description: 'Use the public request form.',
        icon: 'pi-globe',
        introduction:
          'Your service company can share a public request form. You can use it without creating an account or signing in.',
        callout:
          'For immediate danger, shut off utilities if it is safe and contact emergency services. Do not wait for a reply to a request.',
        steps: [
          {
            text: 'Open the public request form link of your service company (it ends with the company name after /request/). The form shows that you are continuing as a guest. If it says This request form is not available, the link cannot be used.',
          },
          {
            text: 'In Contact, enter your first and last name, Email address and Mobile phone. Choose at least one way to get updates: Email or Text message.',
          },
          {
            text: 'In Property, choose the kind of place and enter the address, city, state and ZIP code. Add access instructions if they help.',
          },
          {
            text: 'In Service details, choose the service type, describe the problem, set the urgency and add photos or documents if useful.',
          },
          { text: 'In Availability, choose when you prefer the visit and add notes if you wish.' },
          {
            text: 'In Review, check everything, confirm the details are accurate and select Submit request.',
          },
        ],
        whatHappensNext:
          'You see Request received with your request number, and the page says a confirmation will be sent to your email. Keep the number for your records. Select Submit another request if you need to send a second one.',
      },
      {
        slug: 'follow-your-requests',
        title: 'Follow your requests',
        description: 'Check the status of what you asked for.',
        icon: 'pi-list',
        introduction:
          'The Requests page lists everything you have asked for, with its current status.',
        steps: [
          { text: 'Select Requests in the top bar.' },
          {
            text: 'Review the table: request number, title, property, status and the date you submitted it. Use the Property filter if you have several locations.',
          },
          { text: 'Select View on a request to see its details.' },
          {
            text: 'The detail page shows the category, service, urgency, property, preferred availability and your description.',
          },
          {
            text: 'If a quote was prepared for the request, it appears in a Quote section with its status.',
          },
        ],
        whatHappensNext:
          'The status changes as your service company works on the request. Select Request a service on this page to send a new one.',
      },
    ],
  },
  {
    label: 'APPOINTMENTS',
    articles: [
      {
        slug: 'view-your-appointments',
        title: 'View your appointments',
        description: 'See upcoming and past visits.',
        icon: 'pi-calendar',
        introduction: 'The Appointments page shows your scheduled visits and your service history.',
        steps: [
          { text: 'Select Appointments in the top bar.' },
          { text: 'Use the Upcoming and Past tabs to switch between future and earlier visits.' },
          {
            text: 'Review the date, time, service, work order, technician, property and status. A visit without a technician shows To be assigned.',
          },
          { text: 'Select View to open an appointment.' },
          {
            text: 'The detail page shows the date, the arrival window or time, the technician and the property, plus a progress tracker. When a report is available, select View report.',
          },
        ],
        whatHappensNext: 'Your next visit also appears on Home in the Upcoming appointment card.',
      },
      {
        slug: 'request-a-reschedule',
        title: 'Request a reschedule',
        description: 'Ask for a different date or time.',
        icon: 'pi-clock',
        introduction:
          'If a visit no longer suits you, ask your service company for a different date or time from the appointment.',
        callout:
          'Your appointment does not change until your service company confirms. Request reschedule only appears while a change can still be requested.',
        steps: [
          {
            text: 'Open the appointment from Appointments, or from the Upcoming appointment card on Home.',
          },
          { text: 'Select Request reschedule.' },
          { text: 'Choose a Preferred date and a Time window.' },
          { text: 'Add a Reason so your service company understands what you need.' },
          { text: 'Select Send request. Select Cancel to close without sending.' },
        ],
        whatHappensNext:
          'The appointment shows Reschedule requested with the date you asked. Your service company reviews it and contacts you.',
      },
    ],
  },
];
