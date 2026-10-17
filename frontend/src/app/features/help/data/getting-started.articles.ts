import { HelpSection } from '../models/help.model';

/** Getting started articles. Content describes only behavior FieldOps implements (BR-10). */
export const GETTING_STARTED_SECTIONS: readonly HelpSection[] = [
  {
    label: 'SET UP YOUR WORKSPACE',
    articles: [
      {
        slug: 'fieldops-basics',
        title: 'FieldOps basics',
        description: 'Understand the main areas and workflow.',
        icon: 'pi-book',
        introduction:
          'FieldOps organizes your field service work in the sidebar. Each area matches a stage of the work, from the first customer request to the final payment.',
        callout:
          'The sidebar only lists the areas your role can use. Administration appears for Owner and Viewer roles.',
        steps: [
          {
            text: 'Start on the Dashboard (or Overview, depending on your role) to see the current state of the business.',
          },
          {
            text: 'Under Operations, use Requests to receive and review customer work, Quotes to price it, Jobs to follow approved work and Schedule to assign visits to your team.',
          },
          {
            text: 'Under People, use Customers to manage customers and their properties, and Team to manage technician profiles, skills and availability.',
          },
          {
            text: 'Under Finance, use Products and services for your catalog, Invoices to review completed work, send invoices and record payments, and Reports and Audit log when your role includes them.',
          },
          {
            text: 'Use the bell icon or Notifications to follow what changed, and open your name at the top right for My profile, Account settings and Help & support.',
          },
        ],
        whatHappensNext:
          'A typical job moves from a request to a quote, then to a job that is scheduled and completed by a technician, and finally to an invoice and a payment. The next articles explain each step.',
      },
      {
        slug: 'set-up-your-organization',
        title: 'Set up your organization',
        description: 'Review your company details and defaults.',
        icon: 'pi-building',
        introduction:
          'Company settings hold the details and defaults your whole organization uses, such as your company profile, taxes and currency, and document numbering.',
        callout:
          'Administration is available to Owner and Viewer roles. If you do not see it in the sidebar, ask an Owner to make the change.',
        steps: [
          { text: 'Select Administration in the sidebar to open Company setup.' },
          {
            text: 'In Company profile, review the legal business name, display name, contact details, tax ID, address and company logo.',
          },
          { text: 'In Taxes & currency, choose your currency and default tax rate.' },
          {
            text: 'In Document numbering, review the prefixes and the next invoice number used for quotes, work orders and invoices.',
          },
          {
            text: 'Review Business hours and Bank transfer details if you need them, then select Save changes.',
          },
        ],
        whatHappensNext:
          'Your defaults are used on new quotes, work orders and invoices. Branches and users are managed in their own sections of Administration.',
      },
      {
        slug: 'create-and-manage-branches',
        title: 'Create and manage branches',
        description: 'Organize work by service location.',
        icon: 'pi-map-marker',
        introduction:
          'Branches let you organize customers, team members and work by service location. Your first branch is created when you register your organization.',
        steps: [
          { text: 'Select Administration, then open the Branches section of Company setup.' },
          { text: 'Select Add branch to open the New branch panel.' },
          {
            text: 'Enter the branch name and code, contact phone and email, time zone, address and service area ZIP codes.',
          },
          {
            text: 'Turn on Use company billing settings to inherit tax rates, currency and document numbering from company settings.',
          },
          { text: 'Select Create branch.' },
          {
            text: 'To change a branch later, select it in the list, update its details and select Update branch. Use Deactivate branch to stop new records at that location.',
          },
        ],
        whatHappensNext:
          'The branch appears in the list and in branch filters across FieldOps. Deactivating a branch prevents new records but preserves all historical data.',
      },
    ],
  },
  {
    label: 'FIRST STEPS',
    articles: [
      {
        slug: 'invite-your-team',
        title: 'Invite your team',
        description: 'Add staff and assign the right role.',
        icon: 'pi-users',
        introduction:
          'Invite colleagues by email and give each person a role that controls what they can see and do.',
        callout:
          'The last Owner cannot be suspended or downgraded. Suspending access preserves work orders and audit history.',
        steps: [
          { text: 'Select Administration, then Users & permissions.' },
          { text: 'Select Invite user.' },
          { text: 'Enter the email address, first name and last name.' },
          {
            text: 'Choose a Role. The Permission matrix on the page shows what each predefined role can do.',
          },
          {
            text: 'Choose the branch access: All branches or specific branches. Turn on Link operational team profile after acceptance when the person is a technician.',
          },
          { text: 'Select Send invitation.' },
        ],
        whatHappensNext:
          'The person receives an invitation by email and appears in the Users list. From the row menu you can use Resend invitation, Edit access or Suspend access.',
      },
      {
        slug: 'add-your-first-customer',
        title: 'Add your first customer',
        description: 'Create a customer and service property.',
        icon: 'pi-user',
        introduction:
          'Customers hold contact details, service properties and history. Create one before you record a request for them.',
        steps: [
          { text: 'Select Customers in the sidebar, then New customer.' },
          {
            text: 'Choose the customer type, then enter the name (or company name), email, mobile phone and preferred communication.',
          },
          {
            text: 'Add the address, service instructions, branch, tags and an internal note if they apply.',
          },
          {
            text: 'If FieldOps shows Possible existing customer, review the match. Choose View existing customer, Use different details or Create anyway.',
          },
          { text: 'Select Create customer.' },
          {
            text: 'Open the customer and, in Properties, select Add property to record each service location with its address, branch and service instructions.',
          },
        ],
        whatHappensNext:
          'The customer and its properties can be chosen when you create a request. You can also bring in many customers at once with Import customers.',
      },
      {
        slug: 'configure-products-and-services',
        title: 'Configure products and services',
        description: 'Prepare reusable items for quotes and invoices.',
        icon: 'pi-box',
        introduction:
          'The catalog stores the services and materials you sell so you can reuse them instead of typing prices each time.',
        steps: [
          { text: 'Select Products and services in the sidebar.' },
          { text: 'Select Add item.' },
          { text: 'Choose the Type, then enter a name, description and category.' },
          {
            text: 'Enter the unit price and unit cost. FieldOps calculates the estimated margin for you.',
          },
          { text: 'Set Taxable and Active as needed and save the item.' },
          {
            text: 'Use Manage categories to organize the catalog and Import CSV to load many items at once.',
          },
        ],
        whatHappensNext:
          'Active items can be picked when you add services and materials to a quote, and when you plan materials on a work order. Invoice totals come from the approved quote and the completed work.',
      },
    ],
  },
];
