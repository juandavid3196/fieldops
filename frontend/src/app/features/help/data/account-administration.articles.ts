import { HelpSection } from '../models/help.model';

/** Account & administration articles. Content describes only behavior FieldOps implements (BR-10). */
export const ACCOUNT_ADMINISTRATION_SECTIONS: readonly HelpSection[] = [
  {
    label: 'YOUR ACCOUNT',
    articles: [
      {
        slug: 'update-your-profile',
        title: 'Update your profile',
        description: 'Change your name, phone and photo.',
        icon: 'pi-user',
        introduction:
          'Your profile shows your name, contact details and photo. You can change them at any time from your account settings.',
        callout:
          'Your email address, organization and role cannot be changed here. An administrator manages your role.',
        steps: [
          {
            text: 'Open your name at the top right and select My profile. Technicians open More and select My profile.',
          },
          {
            text: 'To change your photo, select Change photo and choose a JPG or PNG file of 5 MB or less. Select Remove photo to delete it.',
          },
          {
            text: 'Edit your First name and Last name. Add or update your Phone number if you wish.',
          },
          { text: 'Select Save changes. Select Cancel to leave without saving.' },
        ],
        whatHappensNext:
          'Your updated name and photo appear across FieldOps. Your Membership details stay read-only.',
      },
      {
        slug: 'change-your-password',
        title: 'Change your password',
        description: 'Keep your account secure.',
        icon: 'pi-lock',
        introduction:
          'Change your password from Account settings. You need your current password to set a new one.',
        steps: [
          {
            text: 'Open your name at the top right and select Account settings, then Security. Technicians open More and select Account settings.',
          },
          { text: 'Enter your Current password.' },
          {
            text: 'Enter your New password and repeat it in Confirm new password. The Password requirements list shows which rules are met.',
          },
          { text: 'Select Update password.' },
        ],
        whatHappensNext:
          'Changing your password keeps you signed in on this device and signs you out on other devices.',
      },
    ],
  },
  {
    label: 'ADMINISTRATION',
    articles: [
      {
        slug: 'manage-users-and-permissions',
        title: 'Manage users and permissions',
        description: 'Invite users and review role access.',
        icon: 'pi-users',
        introduction:
          'Users & permissions lists everyone who can sign in to your organization, with their role and branch access. Invite new people and change or suspend access here.',
        callout:
          'Only owners can invite users and change access. Viewers can open the page but it is read-only. Other roles do not see Administration.',
        steps: [
          { text: 'Select Administration in the sidebar, then Users & permissions.' },
          {
            text: 'Use the filters to find people by role, branch or status: Active, Pending invitation or Suspended.',
          },
          { text: 'Select Invite user.' },
          {
            text: 'Enter the Email address, First name and Last name, choose the Role and set the Branch access. For Operations Manager, Dispatcher and Technician roles you can also switch on Link operational team profile after acceptance.',
          },
          {
            text: 'Choose how long the invitation lasts in Invitation expires in, then select Send invitation.',
          },
          {
            text: 'To change someone later, select Edit access on their row. The row menu offers Suspend access and Reactivate, and for pending invitations Resend invitation and Revoke invitation.',
          },
        ],
        whatHappensNext:
          'The person receives an invitation email and accepts it to start using FieldOps. The last Owner cannot be suspended or downgraded. Roles are Owner, Operations Manager, Dispatcher, Technician, Accounting and Viewer; the Permission matrix at the bottom of the page shows what each one can do.',
      },
      {
        slug: 'manage-technician-skills-and-availability',
        title: 'Manage technician skills and availability',
        description: 'Set the skills and hours used for assignment.',
        icon: 'pi-id-card',
        introduction:
          'Technician profiles hold the skills and working hours that help you choose who to assign. Open Team in the sidebar to work with them.',
        callout:
          'Owners and operations managers can edit. Dispatchers see the information read-only, and technicians see only their own profile. The Skills and Availability tabs and Manage skills buttons on the Team page are not available yet; use the profile steps below.',
        steps: [
          {
            text: 'To create a profile, select Add technician profile on the Team page, fill in the details and save.',
          },
          {
            text: 'Open a technician from the list (View in the row menu) to see the profile drawer, then select Edit skills or Edit availability. Both open the Skills & availability page.',
          },
          {
            text: 'In Operational skills, search and add skills, set the level, and select Set as primary for the main one. Use Manage skill list to add skills to your organization.',
          },
          {
            text: 'In Standard weekly availability, set the working days, start and end times, breaks and capacity. Copy Monday to weekdays or Reset can speed this up.',
          },
          {
            text: 'In Availability exceptions, select Add exception to override the weekly schedule for a date.',
          },
          { text: 'Select Save changes.' },
        ],
        whatHappensNext:
          'Dispatchers can compare technicians by skills and availability when assigning work. FieldOps never assigns a technician automatically. The Overview, Schedule and Job history tabs on this page are not available yet.',
      },
      {
        slug: 'manage-customer-records',
        title: 'Manage customer records',
        description: 'Find, edit, import and archive customers.',
        icon: 'pi-address-book',
        introduction:
          'Customers keeps every customer, their properties and notes in one place. Search, add or import customers and archive the ones you no longer work with.',
        callout:
          'Owners and dispatchers can add and edit customers; only owners can import. Other roles see them read-only. On a customer page, the Requests, Quotes, Jobs and Invoices tabs only show a coming soon message; Overview and Activity work.',
        steps: [
          {
            text: 'Select Customers in the sidebar. Use the All customers, Leads, Active and Archived tabs and the search and filters to find a customer.',
          },
          {
            text: 'Select New customer to add one. If FieldOps finds a Possible existing customer, review it and choose View existing customer or Create anyway.',
          },
          {
            text: 'To add many at once, select Import customers, download the CSV template, drop your file and review any row errors or duplicate warnings before importing.',
          },
          {
            text: 'Open the row menu to View, Edit, Archive or Reactivate a customer. Select a name to open its page.',
          },
          {
            text: 'On the customer page, use Edit customer for details. The properties card has Add property, Edit, Set as primary and Archive, and Internal notes lets you add notes.',
          },
        ],
        whatHappensNext:
          'Archived customers move to the Archived tab and can be reactivated. Their history is kept.',
      },
      {
        slug: 'invite-a-customer-to-the-portal',
        title: 'Invite a customer to the portal',
        description: 'Give a customer access to their portal.',
        icon: 'pi-send',
        introduction:
          'The customer portal lets a customer sign in to see their own information. Invite the customer from their page and they activate access from an email.',
        callout:
          'Owners and dispatchers can invite. If the contact cannot receive an invitation, FieldOps shows a message and nothing is sent.',
        steps: [
          { text: 'Select Customers and open the customer.' },
          {
            text: 'In the Contact details card, check the email address, then select Invite to portal (shown while Portal access is Not invited).',
          },
          {
            text: 'The card shows Portal access as Invitation sent with its expiry date. Select Resend invitation to send it again.',
          },
          {
            text: 'To stop access, select Remove portal access and confirm. The customer is signed out of the portal.',
          },
        ],
        whatHappensNext:
          'The customer receives an email that opens the portal activation page, confirms their name, sets a password (or enters their current one if they already have an account) and selects Activate access. Portal access then shows as Active since the activation date.',
      },
    ],
  },
];
