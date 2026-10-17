import { HelpSection } from '../models/help.model';

/** Scheduling & team articles. Content describes only behavior FieldOps implements (BR-10). */
export const SCHEDULING_TEAM_SECTIONS: readonly HelpSection[] = [
  {
    label: 'SCHEDULING',
    articles: [
      {
        slug: 'schedule-a-visit',
        title: 'Schedule a visit',
        description: 'Set the date, time and expected duration.',
        icon: 'pi-calendar',
        introduction:
          'Use the dispatch calendar to give a job visit a date and a time window. Visits waiting for a date are listed in the Unscheduled panel.',
        steps: [
          { text: 'Select Schedule in the sidebar to open the Dispatch calendar.' },
          {
            text: 'In the Unscheduled panel, search or filter the list and select the visit you want to plan.',
          },
          {
            text: 'In the Assign work order panel, choose the Date, the Start time and the End time. Add a Preferred arrival window if the customer needs one.',
          },
          {
            text: 'Times use the branch time zone shown in the panel. Review the customer notification options.',
          },
          { text: 'Select Schedule.' },
        ],
        whatHappensNext:
          'The visit moves from the Unscheduled panel to the calendar. Assign a technician to complete the plan.',
      },
      {
        slug: 'assign-a-technician',
        title: 'Assign a technician',
        description: 'Match the visit with an available team member.',
        icon: 'pi-users',
        introduction:
          'You choose the technicians for a visit yourself. FieldOps never assigns a technician automatically, but it shows workload and conflicts to help you decide.',
        callout:
          "Required skills of the job and each technician's skills and availability are managed in Team.",
        steps: [
          { text: 'Open the visit from the Unscheduled panel or from the calendar.' },
          {
            text: 'In Assign to technicians, select up to five technicians and mark one as the Primary technician.',
          },
          {
            text: 'Review Workload and route impact. If FieldOps reports conflicts, add the Justification it asks for to confirm anyway.',
          },
          {
            text: 'Choose whether to email the customer when it is scheduled and whether to send technician details to the customer.',
          },
          { text: 'Select Assign (or Assign & notify when a notification will be sent).' },
        ],
        whatHappensNext:
          "The visit appears on the calendar for those technicians and in the primary technician's Today's jobs.",
      },
      {
        slug: 'reschedule-or-cancel-a-visit',
        title: 'Reschedule or cancel a visit',
        description: 'Update the plan while keeping its history.',
        icon: 'pi-clock',
        introduction:
          'You can change the date, time or technicians of a scheduled visit from the calendar. Cancelling a visit is only partly available.',
        callout:
          'The Schedule does not have a Cancel visit control yet. Contact support if a visit has to be cancelled.',
        steps: [
          { text: 'Select Schedule and find the visit on the calendar.' },
          { text: 'Select the visit to open the Assign work order panel.' },
          {
            text: 'Change the Date, Start time, End time or technicians. Add a Justification if FieldOps reports conflicts.',
          },
          { text: 'Select Update (or Update & notify when the customer will be notified).' },
        ],
        whatHappensNext:
          'The calendar shows the visit in its new slot. The job keeps its visit history.',
      },
    ],
  },
  {
    label: 'FIELD WORK',
    articles: [
      {
        slug: 'update-a-visit-status',
        title: 'Update a visit status',
        description: 'Move a visit from scheduled to completed.',
        icon: 'pi-file-edit',
        introduction:
          "Technicians move a visit forward from Today's jobs. Each step changes the status that the office sees.",
        steps: [
          {
            text: "The primary technician opens Today's jobs and selects the visit. The visit starts as Assigned.",
          },
          { text: 'Select Start travel. The visit becomes On the way.' },
          { text: "On arrival, select I've arrived." },
          { text: 'Select Start job. The visit becomes In progress.' },
          {
            text: 'Use Pause when work stops and Resume to continue. A paused visit shows Paused.',
          },
          { text: 'When the work is finished, select Review & complete and then Complete job.' },
        ],
        whatHappensNext:
          'Office users see the visit status in Jobs and on the job page under Visits, and the job is ready for completion review.',
      },
      {
        slug: 'track-time-on-a-visit',
        title: 'Track time on a visit',
        description: 'Start, pause and finish technician time entries.',
        icon: 'pi-stopwatch',
        introduction:
          'FieldOps measures the work time of a visit while the technician works on it. A separate Time page is not available yet.',
        callout:
          'Manual time entries are not available yet. Contact support if the recorded time needs to be corrected.',
        steps: [
          { text: 'The technician selects Start job on the visit. The work time starts.' },
          {
            text: 'The banner shows In progress with the elapsed work time. Select Pause to stop the clock.',
          },
          { text: 'A paused visit shows Paused. Select Resume to continue.' },
          {
            text: 'Select Review & complete. The Summary shows the Work time that will be saved.',
          },
        ],
        whatHappensNext:
          'The work time is kept with the completed visit and appears in the completion review used for invoicing.',
      },
      {
        slug: 'use-the-schedule-calendar',
        title: 'Use the schedule calendar',
        description: 'Review team workload by day or week.',
        icon: 'pi-calendar',
        introduction:
          "The Dispatch calendar shows each technician's visits, time off and conflicts so you can balance the team's workload.",
        callout: 'Map view is not available yet.',
        steps: [
          { text: 'Select Schedule in the sidebar.' },
          { text: 'Use Day or Week to change the view and Today to return to the current date.' },
          {
            text: 'Use Filters to narrow the calendar by Branch, Team, Skills and Status.',
          },
          {
            text: 'Use the Legend to read the colors for scheduled, proposed, conflict, time off and unassigned items.',
          },
          { text: 'Select a visit to open its details and change the plan.' },
        ],
        whatHappensNext:
          "Any change you save in a visit panel is reflected on the calendar. Use Team to see a technician's availability and capacity.",
      },
    ],
  },
];
