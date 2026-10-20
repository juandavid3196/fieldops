import { HelpSection } from '../models/help.model';

/** Technician field app articles. Content describes only behavior FieldOps implements (BR-10). */
export const TECHNICIAN_FIELD_APP_SECTIONS: readonly HelpSection[] = [
  {
    label: "TODAY'S JOBS",
    articles: [
      {
        slug: 'use-todays-jobs',
        title: "Use Today's jobs",
        description: 'Review your route and the next job.',
        icon: 'pi-briefcase',
        introduction:
          "Today's jobs is the first page technicians see after signing in. It shows your visits for the day and highlights the one to do next.",
        callout:
          'The bottom bar also lists Schedule, Time and Messages. Messages shows a badge when you have unread messages.',
        steps: [
          {
            text: 'Read the summary at the top: the number of jobs, scheduled hours and jobs remaining, plus the progress bar of completed jobs.',
          },
          {
            text: 'In the NEXT card (IN PROGRESS once the visit is on the way, in progress or paused), check the job title, customer, address, service category and arrival window.',
          },
          { text: 'Select Call to phone the customer, or Directions to open the address in maps.' },
          { text: 'Select Start travel when you leave for the job.' },
          { text: "Select View job details, or any row under Today's route, to open the job." },
          { text: 'Use the refresh icon to reload the list.' },
        ],
        whatHappensNext:
          'After Start travel the job moves to On the way. Open it to follow the steps on site.',
      },
      {
        slug: 'open-a-job-and-read-its-details',
        title: 'Open a job and read its details',
        description: 'Check access, scope and instructions.',
        icon: 'pi-file',
        introduction:
          'The job page gathers what you need before you arrive: where to go, how to get in and what work to do.',
        callout:
          'Only the primary technician manages travel for a job. Other assigned technicians can read the job but cannot start travel.',
        steps: [
          {
            text: 'Check the progress steps at the top: Scheduled, On the way, Arrived and In progress.',
          },
          {
            text: 'Review the job header for the title, customer, address and scheduled window. Use Call or Open directions if you need them.',
          },
          {
            text: 'Read Access details for entry instructions, the customer contact preference and any warning about active damage.',
          },
          {
            text: 'Use the Overview tab for job instructions, the dispatch note, scope of work, required skills and planned materials.',
          },
          {
            text: 'Use the Tasks, Materials and Photos tabs to see the checklist, planned materials and assessment photos.',
          },
        ],
        whatHappensNext:
          'Tasks, materials and photos become editable once you start the job. Before that they are read-only.',
      },
      {
        slug: 'use-the-technician-menu',
        title: 'Use the technician menu',
        description: 'Reach your profile, account and help.',
        icon: 'pi-user',
        introduction:
          'The technician menu is the More button at the right of the bottom bar. It holds the pages that are not part of your daily route.',
        callout:
          'There is no page with your past jobs. FieldOps only shows the jobs scheduled for today.',
        steps: [
          { text: 'Select More in the bottom bar.' },
          { text: 'Select My profile to open your technician profile.' },
          { text: 'Select Account settings to manage your own account.' },
          { text: 'Select Help & support to open this help center.' },
          {
            text: 'Select Sign out when you finish.',
          },
        ],
        whatHappensNext: 'Select Today in the bottom bar to return to your route.',
      },
    ],
  },
  {
    label: 'ON SITE',
    articles: [
      {
        slug: 'travel-arrive-and-start-a-job',
        title: 'Travel, arrive and start a job',
        description: 'Move a visit from scheduled to in progress.',
        icon: 'pi-map-marker',
        introduction:
          'Three buttons move a visit through its steps. Each one records the time so the office can follow your progress.',
        callout: 'Start job stays disabled until your arrival is recorded.',
        steps: [
          {
            text: 'Select Start travel on Today or on the job page when you leave. The status changes to On the way.',
          },
          {
            text: "When you reach the site, select I've arrived. The button then shows the arrival time.",
          },
          {
            text: 'Select Start job to begin the work. The status changes to In progress and the labor time starts.',
          },
        ],
        whatHappensNext:
          'The job page switches to the working view with Tasks, Materials, Photos, Notes and a time summary.',
      },
      {
        slug: 'complete-tasks-and-add-materials',
        title: 'Complete tasks and add materials',
        description: 'Track the checklist and materials used.',
        icon: 'pi-check-square',
        introduction:
          'While the job is in progress you keep the checklist and the materials used up to date.',
        callout:
          'Only the primary technician can edit the job. Other technicians see it read-only.',
        steps: [
          {
            text: 'Tick each task in Task checklist as you finish it. Tasks marked Required must be completed before you can review the job.',
          },
          { text: 'Select the comment icon on a task to save a note for it.' },
          { text: 'Select Add task to create an extra task, type its name and select Add.' },
          {
            text: 'In Materials used, compare Planned with Used and change the quantity with the minus and plus buttons or by typing it.',
          },
          {
            text: 'Select Add material, then choose From catalog or Custom material, set the quantity and select Add material.',
          },
        ],
        whatHappensNext:
          'The page shows whether materials match the planned quantities. The review step lists the final tasks and materials.',
      },
      {
        slug: 'add-photos-and-notes',
        title: 'Add photos and notes',
        description: 'Record before and after evidence.',
        icon: 'pi-camera',
        introduction:
          'Photos and notes document the work for the office and for the completion report.',
        callout:
          'Photos must be JPEG or PNG images. You need at least one Before and one After photo to review the job.',
        steps: [
          { text: 'In Photos & evidence, select Add photo.' },
          {
            text: 'Choose Before or After, then pick or take the image. A progress bar shows the upload.',
          },
          { text: 'Select a photo to view it larger. You can delete photos you added.' },
          {
            text: 'Write in Technician notes. They are visible to the office and included in the completion report.',
          },
          {
            text: 'Notes save automatically when you leave the field, and the page confirms with Saved.',
          },
        ],
        whatHappensNext:
          'When both Before and After photos exist, the page shows Before and after photos added.',
      },
      {
        slug: 'pause-resume-or-report-an-issue',
        title: 'Pause, resume or report an issue',
        description: 'Handle breaks and problems on site.',
        icon: 'pi-pause',
        introduction: 'Use Pause for a break and Report issue when something needs the office.',
        callout:
          'Issue reporting is coming soon. For now, Report issue only offers Call office. Use it, or contact support, for urgent problems.',
        steps: [
          {
            text: 'Select Pause in the status banner to stop the labor time. The banner shows Paused.',
          },
          { text: 'Select Resume to continue working.' },
          {
            text: 'Select Report issue in the banner, or Report delay or issue before starting, to open the dialog.',
          },
          { text: 'Select Call office to phone dispatch, then Close.' },
        ],
        whatHappensNext:
          'The Time summary shows labor time and break time so you can check your hours.',
      },
    ],
  },
  {
    label: 'COMPLETE THE JOB',
    articles: [
      {
        slug: 'review-and-complete-a-job',
        title: 'Review and complete a job',
        description: 'Confirm the work and the customer acknowledgment.',
        icon: 'pi-check-circle',
        introduction:
          'Review & complete confirms the work is ready and records how the customer acknowledged it.',
        callout:
          'Review & complete stays disabled until required tasks are done and Before and After photos are added. Use Save and exit to leave and continue later.',
        steps: [
          { text: 'Select Review & complete on the job page.' },
          {
            text: 'Check the Completion checklist, Materials used, Evidence photo counts, the Technician completion note and the work time.',
          },
          { text: 'When the page says Ready to complete, select Continue to acknowledgment.' },
          {
            text: 'Choose a method: Signature obtained, Customer not available, Customer declined to sign or Remote confirmation.',
          },
          {
            text: 'Fill the fields shown for that method, such as signer name, relationship, signature or a comment.',
          },
          { text: 'Select Complete job.' },
        ],
        whatHappensNext:
          'A signature confirms service acknowledgment only. It does not confirm payment.',
      },
    ],
  },
];
