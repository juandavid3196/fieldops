import { HelpSection } from '../models/help.model';

/** Dashboards and insights articles. Content describes only behavior FieldOps implements (BR-10). */
export const DASHBOARDS_INSIGHTS_SECTIONS: readonly HelpSection[] = [
  {
    label: 'DASHBOARDS',
    articles: [
      {
        slug: 'read-the-operations-dashboard',
        title: 'Read the Operations Dashboard',
        description: "Monitor today's visits and technicians.",
        icon: 'pi-th-large',
        introduction:
          'The Operations Dashboard shows how the day is going: visits, people and anything that needs action.',
        callout:
          'Owner, Operations manager and Dispatcher land on the Dashboard. Operations manager and Dispatcher see this Operations view; the Owner can switch between Business and Operations.',
        steps: [
          {
            text: 'Choose a Branch and a Date, or select Today to return to the current day. Select Filters to narrow by Technician or Service category.',
          },
          {
            text: 'Read the key figures: jobs, Unassigned, At risk / delayed, Technician utilization and On-time arrival.',
          },
          { text: 'Review the visit list, then Needs attention for items that require follow-up.' },
          { text: 'Check Technician availability to see who is working and who is free.' },
          {
            text: 'Scroll to Estimated vs actual duration, Service performance and Live operational activity for trends.',
          },
          {
            text: 'Select Open dispatch board to assign or adjust visits, and the refresh icon to reload the data.',
          },
        ],
        whatHappensNext: 'The links in each card open the related jobs, schedule or team page.',
      },
      {
        slug: 'read-the-business-overview',
        title: 'Read the Business Overview',
        description: 'Follow revenue, pipeline and cash flow.',
        icon: 'pi-chart-line',
        introduction:
          'The Business Overview summarizes financial results and capacity for a chosen period.',
        callout:
          'The Business view is available to the Owner only. Switch to it with the Business / Operations control on the Dashboard.',
        steps: [
          {
            text: 'Choose a Branch and a Date range: This month, Last month, Last 30 days, This quarter, This year or Custom.',
          },
          {
            text: 'Turn on Compare to previous period to see changes against the previous period.',
          },
          { text: 'Read the key figures, then Revenue & estimated profit and Cash flow snapshot.' },
          {
            text: 'Review Sales pipeline, Branch performance, Service profitability estimates and Team & capacity.',
          },
          { text: 'Check Owner attention for alerts that need a decision.' },
          { text: 'Select Export report to download the report for the period.' },
        ],
        whatHappensNext:
          'Profit and profitability figures are estimates based on the data recorded in FieldOps.',
      },
      {
        slug: 'use-the-overview-page',
        title: 'Use the Overview page',
        description: 'Know what you see without a dashboard.',
        icon: 'pi-home',
        introduction: 'Roles without a dashboard land on the Overview welcome page instead.',
        callout:
          "Accounting and Viewer roles land on Overview. Technicians use Today's jobs, and the Owner, Operations manager and Dispatcher use the Dashboard.",
        steps: [
          { text: 'Sign in to open Overview. It greets you with Welcome and your first name.' },
          { text: 'Read your Organization and Role to confirm which account you are using.' },
          {
            text: 'Use the sidebar to open the areas your role can access, such as Reports or Audit log.',
          },
        ],
        whatHappensNext:
          'Overview has no charts or figures. Open Reports for business data when your role includes it.',
      },
    ],
  },
  {
    label: 'REPORTS & ACTIVITY',
    articles: [
      {
        slug: 'use-the-reports-workspace',
        title: 'Use the Reports workspace',
        description: 'Filter, compare and export business reports.',
        icon: 'pi-chart-bar',
        introduction:
          'Reports lets you pick a report, filter it by period and branch, compare it and export it.',
        callout:
          'Owner, Operations manager, Dispatcher and Accounting can open Reports, and each sees only some reports. Job profitability and Invoice aging: Owner, Operations manager, Accounting. Quote conversion, Completed jobs & on-time performance and Technician utilization: Owner, Operations manager, Dispatcher.',
        steps: [
          {
            text: 'Open Reports and choose a report from the catalog. Use Search reports, Favorites or Recently viewed to find one faster.',
          },
          {
            text: 'Set the Date range and Branch. Turn on Compare to previous period to see the change.',
          },
          {
            text: 'Use the filters under the report name, then select Apply filters, or Reset to clear them.',
          },
          {
            text: 'Read the summary figures, the chart and the table. Sort columns and change pages in the table.',
          },
          { text: 'Select the star to add the report to Favorites.' },
          {
            text: 'Select Export CSV or Download PDF to export. Export history opens a list of earlier exports; files are not stored, so select Open report to run one again.',
          },
        ],
        whatHappensNext:
          'Invoice aging reflects today, so its date range and comparison are turned off.',
      },
      {
        slug: 'review-the-audit-log',
        title: 'Review the Audit log',
        description: 'Trace who changed what and when.',
        icon: 'pi-history',
        introduction:
          'The Audit log records changes made in your organization so you can review who did what and when.',
        callout:
          'The Audit log is available to Owner, Operations manager and Accounting. Events are read-only and cannot be edited or deleted.',
        steps: [
          { text: 'Select Audit log in the sidebar to open the Audit trail page.' },
          {
            text: 'Read the summary cards at the top: Events today, Users active, High-impact changes and Access changes.',
          },
          {
            text: 'Search by user, entity, reference or event ID, or filter by Date, User, Action, Entity, Branch and Impact, then select Apply filters.',
          },
          { text: 'Sort by Timestamp or Impact and move between pages in the table.' },
          {
            text: 'Select View on a row to open the Audit event with its change summary (Before and After) and metadata.',
          },
          { text: 'Select Export CSV to download the events.' },
        ],
        whatHappensNext: 'In the event details you can copy the event ID to share it with support.',
      },
      {
        slug: 'follow-notifications',
        title: 'Follow Notifications',
        description: 'Stay on top of what needs attention.',
        icon: 'pi-bell',
        introduction:
          'Notifications tells you about activity such as requests, quotes, visits, jobs, invoices and payments.',
        callout:
          'Notifications are available to internal roles, but not to technicians. For technicians the bell and Notifications are Coming soon.',
        steps: [
          {
            text: 'Select the bell icon to see recent notifications, or select View all notifications to open the full page.',
          },
          {
            text: 'Choose All notifications or Unread, then search or filter by Type and Date (and Branch when your organization has more than one).',
          },
          {
            text: 'Select a notification to mark it read and, when it has a related record, open that record.',
          },
          { text: 'Use the row menu to mark one as read or unread.' },
          { text: 'Select Mark all as read to clear every unread notification.' },
        ],
        whatHappensNext:
          'The bell shows the unread count. When new notifications arrive, the page offers a Refresh button.',
      },
    ],
  },
];
