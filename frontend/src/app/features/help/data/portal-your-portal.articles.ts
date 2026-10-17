import { HelpSection } from '../models/help.model';

/** Customer portal articles: your portal. Content describes only behavior FieldOps implements (BR-10, BR-14). */
export const PORTAL_YOUR_PORTAL_SECTIONS: readonly HelpSection[] = [
  {
    label: 'GET STARTED',
    articles: [
      {
        slug: 'activate-your-portal-access',
        title: 'Activate your portal access',
        description: 'Accept your invitation and create your password.',
        icon: 'pi-key',
        introduction:
          'Your service company invites you to the customer portal by email. Use the invitation link to activate your access once.',
        callout:
          'If the page says Invitation unavailable, the link can no longer be used. Contact your service company to ask for a new invitation.',
        steps: [
          { text: 'Open the invitation email and select its link to open the activation page.' },
          {
            text: 'If the page asks for your Last name, enter it. It only appears when your name is incomplete.',
          },
          {
            text: 'Enter a Password and repeat it in Confirm password. Use 12 to 128 characters that differ from your email.',
          },
          {
            text: 'If you already have a portal password from another company, the page asks for your Current password instead. Enter it and select Link my account.',
          },
          { text: 'Otherwise, select Activate access.' },
        ],
        whatHappensNext:
          'Your access is ready. From then on, use Sign in with your email and password to open your portal.',
      },
      {
        slug: 'sign-in-and-reset-your-password',
        title: 'Sign in and reset your password',
        description: 'Access your portal or recover your access.',
        icon: 'pi-sign-in',
        introduction:
          'Sign in with the email and password you created when you activated your access. If you forget your password, you can reset it by email.',
        callout:
          'The portal has no page for changing your password while signed in. To choose a new one, use Forgot password? on the sign-in page.',
        steps: [
          { text: 'Open the sign-in page of your portal, titled Sign in to your portal.' },
          {
            text: 'Enter your Email and Password. Select Show password if you want to check what you typed.',
          },
          {
            text: 'Select Remember me if you want to stay signed in on this device. Skip it on a shared computer.',
          },
          { text: 'Select Sign in.' },
          {
            text: 'If you forgot your password, select Forgot password?, enter your email and select Send reset link. Open the email, select the link, then choose a new password, confirm it and select Reset password. The page then says Password updated; select Continue to sign in.',
          },
        ],
        whatHappensNext:
          'After signing in you land on Home. The reset link can be used once and expires in 30 minutes; if it expires, request a new link from the same page.',
      },
      {
        slug: 'get-around-your-portal',
        title: 'Get around your portal',
        description: 'Find requests, appointments, quotes and invoices.',
        icon: 'pi-compass',
        introduction:
          'The top bar of the portal gives you access to every area. Home summarizes everything that needs your attention.',
        callout:
          'On a small screen the sections are in a menu. Select Open navigation at the top left to see them.',
        steps: [
          {
            text: 'Use Home, Requests, Appointments, Quotes and Invoices in the top bar to move between areas.',
          },
          {
            text: 'On Home, review the cards for Action required, Payment due, Property, Upcoming appointment, Active requests, Messages & updates and Recent activity. Select Request a service to ask for new work.',
          },
          {
            text: 'Select Help in the top bar to open the Help & support center. To reach your service company, use the Need help? card on Home: Call shows when the company has a phone number and Send a message when it can receive messages.',
          },
          { text: 'Select the bell to open your updates. A number on it shows unread updates.' },
          {
            text: 'Select your name to open the account menu. If you belong to more than one customer account, choose one under Switch account. Select Sign out when you finish.',
          },
        ],
        whatHappensNext:
          'Use Home as your starting point each time you sign in. Anything that needs you, such as a quote to review or an invoice to pay, appears there first.',
      },
    ],
  },
  {
    label: 'YOUR DETAILS',
    articles: [
      {
        slug: 'manage-your-properties',
        title: 'Manage your properties',
        description: 'Keep your service locations up to date.',
        icon: 'pi-map-marker',
        introduction:
          'Properties are the service locations on your account. You can add new ones and update their name and access details.',
        callout:
          'You cannot change an address after a property is created. The page tells you to contact your service company to change it.',
        steps: [
          {
            text: 'On the Your property card on Home, select Manage to open Properties and review your service locations.',
          },
          { text: 'To add one, select Add property (on Properties or on the Your property card).' },
          {
            text: 'Enter a Property name you will recognize and the service address. Add Access instructions if the crew needs them, such as a gate code or where to park.',
          },
          { text: 'Select Add property to save it.' },
          {
            text: 'To update an existing property, select Edit on its card, change the name or access instructions and select Save changes.',
          },
        ],
        whatHappensNext:
          'The property appears in your list and you can pick it when you request a service. Use the Property filter on Home, Requests and Appointments to focus on one location.',
      },
      {
        slug: 'follow-your-updates-and-activity',
        title: 'Follow your updates and activity',
        description: 'See what changed and what you need to do.',
        icon: 'pi-bell',
        introduction:
          'Messages & updates is a feed of what changed on your account, such as quotes, appointments and invoices. Recent activity lists your completed services.',
        callout:
          'Messages & updates is a read-only feed, not a chat. To reach your service company, use the phone number or Send a message on the Need help? card on Home when they are shown.',
        steps: [
          {
            text: 'Select the bell, or View all on the Messages & updates card on Home, to open the feed.',
          },
          {
            text: 'Use the filters All, Action required, Appointments and Billing to narrow the list, and turn on Unread only to see what you have not opened.',
          },
          { text: 'Type in Search updates to find a specific update.' },
          {
            text: 'Select the button on an update to open the related quote, appointment or invoice.',
          },
          {
            text: 'To review past work, select the Recent activity card on Home. The table shows each service with its date, reference, amount and status, and lets you open an invoice, a report or a receipt when one is available.',
          },
        ],
        whatHappensNext:
          'Unread updates are marked in the feed and counted on the bell. When nothing is unread, the feed says You are all caught up.',
      },
    ],
  },
];
