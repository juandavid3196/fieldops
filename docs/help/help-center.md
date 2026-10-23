**English** | [Español](help-center.es.md)

# FieldOps Help & support

## Help for staff (`/help`)

### Popular questions

**How do I create a service request?**

Select Requests in the sidebar, then New request, choose the customer and property, describe the work and select Create request.

**How do I turn an approved quote into a job?**

Open the approved quote in Quotes and select Create work order. Review the job details and select Create work order again to save it.

**How do I assign a technician?**

Open the visit from the Schedule, choose the technicians under Assign to technicians, mark one as primary and select Assign.

**How do I record a payment?**

In Invoices, open the row menu of the invoice and select Record payment. Enter the amount, date and method, then select Record external payment.

### Getting started

Set up your workspace and learn the FieldOps basics.

#### SET UP YOUR WORKSPACE

##### FieldOps basics

_Understand the main areas and workflow._

FieldOps organizes your field service work in the sidebar. Each area matches a stage of the work, from the first customer request to the final payment.

> The sidebar only lists the areas your role can use. Administration appears for Owner and Viewer roles.

1. Start on the Dashboard (or Overview, depending on your role) to see the current state of the business.
2. Under Operations, use Requests to receive and review customer work, Quotes to price it, Jobs to follow approved work and Schedule to assign visits to your team.
3. Under People, use Customers to manage customers and their properties, and Team to manage technician profiles, skills and availability.
4. Under Finance, use Products and services for your catalog, Invoices to review completed work, send invoices and record payments, and Reports and Audit log when your role includes them.
5. Use the bell icon or Notifications to follow what changed, and open your name at the top right for My profile, Account settings and Help & support.

**What happens next?**

A typical job moves from a request to a quote, then to a job that is scheduled and completed by a technician, and finally to an invoice and a payment. The next articles explain each step.

##### Set up your organization

_Review your company details and defaults._

Company settings hold the details and defaults your whole organization uses, such as your company profile, taxes and currency, and document numbering.

> Administration is available to Owner and Viewer roles. If you do not see it in the sidebar, ask an Owner to make the change.

1. Select Administration in the sidebar to open Company setup.
2. In Company profile, review the legal business name, display name, contact details, tax ID, address and company logo.
3. In Taxes & currency, choose your currency and default tax rate.
4. In Document numbering, review the prefixes and the next invoice number used for quotes, work orders and invoices.
5. Review Business hours and Bank transfer details if you need them, then select Save changes.

**What happens next?**

Your defaults are used on new quotes, work orders and invoices. Branches and users are managed in their own sections of Administration.

##### Create and manage branches

_Organize work by service location._

Branches let you organize customers, team members and work by service location. Your first branch is created when you register your organization.

1. Select Administration, then open the Branches section of Company setup.
2. Select Add branch to open the New branch panel.
3. Enter the branch name and code, contact phone and email, time zone, address and service area ZIP codes.
4. Turn on Use company billing settings to inherit tax rates, currency and document numbering from company settings.
5. Select Create branch.
6. To change a branch later, select it in the list, update its details and select Update branch. Use Deactivate branch to stop new records at that location.

**What happens next?**

The branch appears in the list and in branch filters across FieldOps. Deactivating a branch prevents new records but preserves all historical data.

#### FIRST STEPS

##### Invite your team

_Add staff and assign the right role._

Invite colleagues by email and give each person a role that controls what they can see and do.

> The last Owner cannot be suspended or downgraded. Suspending access preserves work orders and audit history.

1. Select Administration, then Users & permissions.
2. Select Invite user.
3. Enter the email address, first name and last name.
4. Choose a Role. The Permission matrix on the page shows what each predefined role can do.
5. Choose the branch access: All branches or specific branches. Turn on Link operational team profile after acceptance when the person is a technician.
6. Select Send invitation.

**What happens next?**

The person receives an invitation by email and appears in the Users list. From the row menu you can use Resend invitation, Edit access or Suspend access.

##### Add your first customer

_Create a customer and service property._

Customers hold contact details, service properties and history. Create one before you record a request for them.

1. Select Customers in the sidebar, then New customer.
2. Choose the customer type, then enter the name (or company name), email, mobile phone and preferred communication.
3. Add the address, service instructions, branch, tags and an internal note if they apply.
4. If FieldOps shows Possible existing customer, review the match. Choose View existing customer, Use different details or Create anyway.
5. Select Create customer.
6. Open the customer and, in Properties, select Add property to record each service location with its address, branch and service instructions.

**What happens next?**

The customer and its properties can be chosen when you create a request. You can also bring in many customers at once with Import customers.

##### Configure products and services

_Prepare reusable items for quotes and invoices._

The catalog stores the services and materials you sell so you can reuse them instead of typing prices each time.

1. Select Products and services in the sidebar.
2. Select Add item.
3. Choose the Type, then enter a name, description and category.
4. Enter the unit price and unit cost. FieldOps calculates the estimated margin for you.
5. Set Taxable and Active as needed and save the item.
6. Use Manage categories to organize the catalog and Import CSV to load many items at once.

**What happens next?**

Active items can be picked when you add services and materials to a quote, and when you plan materials on a work order. Invoice totals come from the approved quote and the completed work.

### Requests, quotes & jobs

Manage customer work from the first request to completion.

#### REQUESTS & QUOTES

##### Create a service request

_Record a new customer request and its details._

A request records what a customer needs. Create one yourself when a customer calls or writes, or review the ones that arrive through your public request form.

> The customer and property must already exist. Add them first from Customers if needed.

1. Select Requests in the sidebar, then New request.
2. Search and choose the Customer, then choose the Contact and the Property.
3. Choose the Category and the Service, and describe the work in Description.
4. Set the Priority and the Availability. Add a preferred date and time window if the customer gave one.
5. Add any scheduling notes and select Create request.

**What happens next?**

The request appears in the New requests column of the board. Open it to schedule an assessment, request information, mark it ready for quote, assign it or change its priority.

##### Build and send a quote

_Add services, pricing and send it for approval._

Quotes are created from a request that is ready for quote. You add the priced work, review the totals and send the quote to the customer by email.

> SMS delivery is not available yet. Quotes are sent by email.

1. Open the request in Requests. When it is in Ready for quote, select Create quote.
2. Add lines with Add service, Add material or Add optional item. Pick items from the catalog and adjust quantity, rate and tax.
3. Use Add discount if needed and review the Quote summary totals.
4. Write the Customer message, add an Internal note if you want one, and review the Terms and Quote expiration.
5. Select Save draft to keep working later.
6. In Customer approval, check the recipient email and the message, then select Send quote and confirm.

**What happens next?**

The customer receives a secure link to review and respond to the quote. Follow its status in Quotes. After it is sent you can use Revise quote or Resend email from the quote page.

##### Record a quote response

_Capture an approval, rejection or clarification._

Customers answer a quote themselves from the secure link they receive: they can approve it, decline it or ask a question. FieldOps records that answer on the quote.

> Staff cannot record a response on behalf of a customer in the app yet. Contact support if you need help with a response you received outside FieldOps.

1. Select Quotes in the sidebar and open the quote.
2. Find the Customer response section. Each response shows its type (Approved, Declined or Question), who sent it, when, and any comment.
3. If the customer asked a question or declined, use Revise quote to prepare a new version and send it again.
4. If the customer did not receive the email, use Resend email. The previous link stops working.

**What happens next?**

An approved quote can be turned into a job with Create work order. A declined or questioned quote can be revised and sent again.

##### Schedule an assessment

_Visit the site before preparing a quote._

An assessment is a site visit to diagnose the work before you quote it. It is optional: if the request already has enough information, choose Mark ready for quote instead.

> SMS notifications are not available yet. The customer can only be notified by email, and only when the request has an email address.

1. Select Requests in the sidebar and open the request.
2. In the request panel (available for New or Needs review requests), select Schedule assessment.
3. In Find a time, review the customer availability, select a technician and check their calendar. Switch between Week, Day and Agenda views.
4. In Assessment visit, set the Date, Arrival window, Estimated duration and Assigned technician. Choose a Branch first if asked.
5. Describe the Purpose for assessment. Add Internal instructions if needed; the customer does not see them.
6. Review the Customer message preview, keep Email on under Notify customer if you want to tell the customer, then select Schedule assessment.

**What happens next?**

The assessment appears in the request panel. When the visit is done, select Complete assessment to record the Diagnosis, the Recommended scope and optional photos, then continue to the quote. To change the visit, use Reschedule in the request panel, or Cancel assessment in the more-actions menu of the request.

#### JOBS & VISITS

##### Create a job from a quote

_Turn approved work into an active job._

When a customer approves a quote, you turn the approved scope into a job. While you prepare it, FieldOps calls it a work order.

1. Select Quotes and open the approved quote.
2. Select Create work order.
3. Review the Job details: work order title, job type, service category, branch, priority, estimated duration and skills required.
4. Add instructions for the assigned technician, the access note, and the tasks and checklist. Use Use checklist template to add saved tasks.
5. Add the required materials, and set the preferred date, arrival window and recurrence if the work repeats.
6. Select Create work order.

**What happens next?**

The job appears in Jobs, ready to schedule, and stays linked to the approved quote. Pricing remains tied to that quote. The next step is to schedule a visit.

##### Add the first visit

_Choose the date, time and work location._

Visits are created together with the job and then scheduled in the Schedule area. There is no separate control to add a visit by hand.

> Extra visits come from the Recurrence option of the work order. If you need another visit that is not covered, contact support.

1. Select Jobs in the sidebar and find the job that needs a visit.
2. Open the row menu and select Schedule visit (shown when the job has a visit to schedule). The Schedule opens with that visit selected.
3. In the Assign work order panel, choose the Date, Start time, End time and, if needed, the Preferred arrival window.
4. The times use the branch time zone. Select Schedule, or Assign if you also chose technicians (the button adds & notify when the customer will be notified).

**What happens next?**

The visit appears on the dispatch calendar and the job shows it under Visits. Assign a technician to complete the plan.

##### Complete a job

_Review finished work and prepare it for billing._

The assigned technician completes the visit from Today's jobs. The office then reviews the finished work before invoicing.

1. The technician opens the visit from Today's jobs, selects Start travel, then I've arrived, then Start job.
2. While working, the technician completes the checklist, records materials and adds before and after photos and notes.
3. The technician selects Review & complete, checks the summary and selects Continue to acknowledgment.
4. The technician records how the customer acknowledged the work (for example a signature) and selects Complete job.
5. In the office, open Jobs and use Review completion in the row menu of the completed job.

**What happens next?**

The job opens in Invoices under Needs review, where you compare approved work with the actual work and create the invoice.

### Scheduling & team

Plan visits and coordinate your field team.

#### SCHEDULING

##### Schedule a visit

_Set the date, time and expected duration._

Use the dispatch calendar to give a job visit a date and a time window. Visits waiting for a date are listed in the Unscheduled panel.

1. Select Schedule in the sidebar to open the Dispatch calendar.
2. In the Unscheduled panel, search or filter the list and select the visit you want to plan.
3. In the Assign work order panel, choose the Date, the Start time and the End time. Add a Preferred arrival window if the customer needs one.
4. Times use the branch time zone shown in the panel. Review the customer notification options.
5. Select Schedule.

**What happens next?**

The visit moves from the Unscheduled panel to the calendar. Assign a technician to complete the plan.

##### Assign a technician

_Match the visit with an available team member._

You choose the technicians for a visit yourself. FieldOps never assigns a technician automatically, but it shows workload and conflicts to help you decide.

> Required skills of the job and each technician's skills and availability are managed in Team.

1. Open the visit from the Unscheduled panel or from the calendar.
2. In Assign to technicians, select up to five technicians and mark one as the Primary technician.
3. Review Workload and route impact. If FieldOps reports conflicts, add the Justification it asks for to confirm anyway.
4. Choose whether to email the customer when it is scheduled and whether to send technician details to the customer.
5. Select Assign (or Assign & notify when a notification will be sent).

**What happens next?**

The visit appears on the calendar for those technicians and in the primary technician's Today's jobs.

##### Reschedule or cancel a visit

_Update the plan while keeping its history._

You can change the date, time or technicians of a scheduled visit from the calendar. Cancelling a visit is only partly available.

> The Schedule does not have a Cancel visit control yet. Contact support if a visit has to be cancelled.

1. Select Schedule and find the visit on the calendar.
2. Select the visit to open the Assign work order panel.
3. Change the Date, Start time, End time or technicians. Add a Justification if FieldOps reports conflicts.
4. Select Update (or Update & notify when the customer will be notified).

**What happens next?**

The calendar shows the visit in its new slot. The job keeps its visit history.

#### FIELD WORK

##### Update a visit status

_Move a visit from scheduled to completed._

Technicians move a visit forward from Today's jobs. Each step changes the status that the office sees.

1. The primary technician opens Today's jobs and selects the visit. The visit starts as Assigned.
2. Select Start travel. The visit becomes On the way.
3. On arrival, select I've arrived.
4. Select Start job. The visit becomes In progress.
5. Use Pause when work stops and Resume to continue. A paused visit shows Paused.
6. When the work is finished, select Review & complete and then Complete job.

**What happens next?**

Office users see the visit status in Jobs and on the job page under Visits, and the job is ready for completion review.

##### Track time on a visit

_Start, pause and finish technician time entries._

FieldOps measures the work time of a visit while the technician works on it. A separate Time page is not available yet.

> Manual time entries are not available yet. Contact support if the recorded time needs to be corrected.

1. The technician selects Start job on the visit. The work time starts.
2. The banner shows In progress with the elapsed work time. Select Pause to stop the clock.
3. A paused visit shows Paused. Select Resume to continue.
4. Select Review & complete. The Summary shows the Work time that will be saved.

**What happens next?**

The work time is kept with the completed visit and appears in the completion review used for invoicing.

##### Use the schedule calendar

_Review team workload by day or week._

The Dispatch calendar shows each technician's visits, time off and conflicts so you can balance the team's workload.

> Map view is not available yet.

1. Select Schedule in the sidebar.
2. Use Day or Week to change the view and Today to return to the current date.
3. Use Filters to narrow the calendar by Branch, Team, Skills and Status.
4. Use the Legend to read the colors for scheduled, proposed, conflict, time off and unassigned items.
5. Select a visit to open its details and change the plan.

**What happens next?**

Any change you save in a visit panel is reflected on the calendar. Use Team to see a technician's availability and capacity.

### Invoices & payments

Create invoices and keep customer balances up to date.

#### INVOICING

##### Create an invoice

_Generate an invoice from completed work._

Invoices are generated from completed jobs after you review the work. FieldOps compares the approved quote with the actual work so you can bill with confidence.

1. Select Invoices in the sidebar, then Needs review. You can also select New invoice on the Invoices page.
2. Select a completed job from the list. Use Review and invoice from a table row.
3. Review the Billing comparison, the work evidence and any variances. Add an Accounting note if you need one.
4. In Invoice details, check the issue date, payment terms, due date, currency and delivery options.
5. Select Generate invoice. If there are variances, confirm Generate draft.

**What happens next?**

Generating creates a draft invoice. Review it before sending. You can find it in Invoices under Drafts.

##### Review completed jobs before invoicing

_Verify finished work against the approved quote._

Completed jobs wait in a review queue before they are invoiced. Compare the approved quote with the actual work, then generate a draft invoice.

> Only owners and accounting can review and generate invoices; other roles see the page read-only. Add adjustment, delivery options and online payment options are shown but not available yet.

1. Select Invoices in the sidebar, then Needs review. The Invoices page also lists jobs under Completed jobs ready to invoice.
2. In the queue, use the All, Variances and Ready views, then select a job.
3. In Billing review, read the Billing comparison. Each line shows a status: Matches quote, Over quote, Under quote, Not in quote or Not tracked. Check the Totals comparison and the Completion verification.
4. Open Work evidence to see the visits and photos, and Audit trail for the history. Add an Accounting note for internal use.
5. In Invoice details, check the Issue date and Payment terms. The Invoice number, Due date, Tax jurisdiction and Currency are shown for reference.
6. Select Generate invoice. If the job has variances, confirm Generate draft. Select Return to quote if the job should not be invoiced yet.

**What happens next?**

Generating creates a draft invoice. A confirmation offers View invoice. Variances stay for review only, and the draft uses the approved quote amounts.

##### Review and send an invoice

_Confirm charges before sending it to the customer._

Review a draft invoice before the customer sees it. Sending delivers the invoice by email.

> Some delivery and payment options are shown as not available yet, such as SMS notification, online payment methods and attachments.

1. Select Invoices, then Drafts, and open the draft invoice.
2. Review the customer, line items, totals, issue date and due date. Use Download PDF to see the document.
3. In the send panel, check the Recipient email, the Payment terms and the Message to customer.
4. Select Send invoice and confirm.

**What happens next?**

The invoice moves to Sent and the customer receives it by email. You can follow its status and payments from Invoices.

##### Report a bank transfer

_Record transfer details awaiting review._

Bank transfers are reported by the customer from their invoice page. Staff do not have a button to report a transfer on their behalf.

> Reviewing a reported transfer is not available in the app yet. Contact support if you need help with one.

1. Make sure your bank transfer details are saved: select Administration and open Bank transfer details in Company setup.
2. Send the invoice. The customer's invoice page shows your Bank transfer instructions and lets them report the transfer.
3. When the money arrives in your account, record it with Record external payment, choosing Bank transfer as the payment method and entering the reference number.

**What happens next?**

Recording the payment updates the invoice balance. See Record a payment for the full steps.

#### PAYMENTS

##### Record a payment

_Apply a received payment to an invoice._

Record money you already received outside FieldOps against an invoice. FieldOps does not charge any card when you do this.

1. Select Invoices and find the invoice. Use the search and filters if needed.
2. Open the row menu and select Record payment.
3. Enter the Amount, the Payment date and the Payment method.
4. Add the Reference number (required for external card payments, bank transfers and checks) and who it was Received by.
5. Choose whether to Send receipt, and add an Internal note if you want one.
6. Select Record external payment.

**What happens next?**

FieldOps creates a payment record and updates the invoice balance. The payment appears under Payments in Invoices.

##### Review overdue invoices

_Find balances that need follow-up._

Overdue invoices are sent invoices with a balance past their due date. Review them regularly to follow up with customers.

1. Select Invoices in the sidebar, then Overdue.
2. Review each invoice's due date, balance and last activity. Use the search and filters to narrow the list.
3. On the Invoices page, check the Aging summary to see balances grouped as Current, 1 – 30 days, 31 – 60 days and 60+ days.
4. Open the row menu and select View invoice to see the details.

**What happens next?**

Once a payment is recorded, the invoice balance updates and it leaves the overdue list when it is paid.

##### Void an invoice

_Cancel an invoice without deleting its history._

Voiding an invoice is not available in the app yet. Invoices keep their full history and are not deleted.

> Contact support to ask for an invoice to be cancelled.

1. Select Invoices and open the invoice to confirm it is the right one.
2. Note the invoice number and the reason you need it cancelled.
3. Select Contact support and send us the invoice number and the reason.

**What happens next?**

The invoice and its history stay unchanged until support handles your request.

### Technician field app

Work your visits from travel to completion in the field.

#### TODAY'S JOBS

##### Use Today's jobs

_Review your route and the next job._

Today's jobs is the first page technicians see after signing in. It shows your visits for the day and highlights the one to do next.

> The bottom bar also lists Schedule, Time and Messages. Messages shows a badge when you have unread messages.

1. Read the summary at the top: the number of jobs, scheduled hours and jobs remaining, plus the progress bar of completed jobs.
2. In the NEXT card (IN PROGRESS once the visit is on the way, in progress or paused), check the job title, customer, address, service category and arrival window.
3. Select Call to phone the customer, or Directions to open the address in maps.
4. Select Start travel when you leave for the job.
5. Select View job details, or any row under Today's route, to open the job.
6. Use the refresh icon to reload the list.

**What happens next?**

After Start travel the job moves to On the way. Open it to follow the steps on site.

##### Open a job and read its details

_Check access, scope and instructions._

The job page gathers what you need before you arrive: where to go, how to get in and what work to do.

> Only the primary technician manages travel for a job. Other assigned technicians can read the job but cannot start travel.

1. Check the progress steps at the top: Scheduled, On the way, Arrived and In progress.
2. Review the job header for the title, customer, address and scheduled window. Use Call or Open directions if you need them.
3. Read Access details for entry instructions, the customer contact preference and any warning about active damage.
4. Use the Overview tab for job instructions, the dispatch note, scope of work, required skills and planned materials.
5. Use the Tasks, Materials and Photos tabs to see the checklist, planned materials and assessment photos.

**What happens next?**

Tasks, materials and photos become editable once you start the job. Before that they are read-only.

##### Use the technician menu

_Reach your profile, account and help._

The technician menu is the More button at the right of the bottom bar. It holds the pages that are not part of your daily route.

> There is no page with your past jobs. FieldOps only shows the jobs scheduled for today.

1. Select More in the bottom bar.
2. Select My profile to open your technician profile.
3. Select Account settings to manage your own account.
4. Select Help & support to open this help center.
5. Select Sign out when you finish.

**What happens next?**

Select Today in the bottom bar to return to your route.

#### ON SITE

##### Travel, arrive and start a job

_Move a visit from scheduled to in progress._

Three buttons move a visit through its steps. Each one records the time so the office can follow your progress.

> Start job stays disabled until your arrival is recorded.

1. Select Start travel on Today or on the job page when you leave. The status changes to On the way.
2. When you reach the site, select I've arrived. The button then shows the arrival time.
3. Select Start job to begin the work. The status changes to In progress and the labor time starts.

**What happens next?**

The job page switches to the working view with Tasks, Materials, Photos, Notes and a time summary.

##### Complete tasks and add materials

_Track the checklist and materials used._

While the job is in progress you keep the checklist and the materials used up to date.

> Only the primary technician can edit the job. Other technicians see it read-only.

1. Tick each task in Task checklist as you finish it. Tasks marked Required must be completed before you can review the job.
2. Select the comment icon on a task to save a note for it.
3. Select Add task to create an extra task, type its name and select Add.
4. In Materials used, compare Planned with Used and change the quantity with the minus and plus buttons or by typing it.
5. Select Add material, then choose From catalog or Custom material, set the quantity and select Add material.

**What happens next?**

The page shows whether materials match the planned quantities. The review step lists the final tasks and materials.

##### Add photos and notes

_Record before and after evidence._

Photos and notes document the work for the office and for the completion report.

> Photos must be JPEG or PNG images. You need at least one Before and one After photo to review the job.

1. In Photos & evidence, select Add photo.
2. Choose Before or After, then pick or take the image. A progress bar shows the upload.
3. Select a photo to view it larger. You can delete photos you added.
4. Write in Technician notes. They are visible to the office and included in the completion report.
5. Notes save automatically when you leave the field, and the page confirms with Saved.

**What happens next?**

When both Before and After photos exist, the page shows Before and after photos added.

##### Pause, resume or report an issue

_Handle breaks and problems on site._

Use Pause for a break and Report issue when something needs the office.

> Issue reporting is coming soon. For now, Report issue only offers Call office. Use it, or contact support, for urgent problems.

1. Select Pause in the status banner to stop the labor time. The banner shows Paused.
2. Select Resume to continue working.
3. Select Report issue in the banner, or Report delay or issue before starting, to open the dialog.
4. Select Call office to phone dispatch, then Close.

**What happens next?**

The Time summary shows labor time and break time so you can check your hours.

#### COMPLETE THE JOB

##### Review and complete a job

_Confirm the work and the customer acknowledgment._

Review & complete confirms the work is ready and records how the customer acknowledged it.

> Review & complete stays disabled until required tasks are done and Before and After photos are added. Use Save and exit to leave and continue later.

1. Select Review & complete on the job page.
2. Check the Completion checklist, Materials used, Evidence photo counts, the Technician completion note and the work time.
3. When the page says Ready to complete, select Continue to acknowledgment.
4. Choose a method: Signature obtained, Customer not available, Customer declined to sign or Remote confirmation.
5. Fill the fields shown for that method, such as signer name, relationship, signature or a comment.
6. Select Complete job.

**What happens next?**

A signature confirms service acknowledgment only. It does not confirm payment.

### Dashboards & insights

Follow your operation, reports and activity across the organization.

#### DASHBOARDS

##### Read the Operations Dashboard

_Monitor today's visits and technicians._

The Operations Dashboard shows how the day is going: visits, people and anything that needs action.

> Owner, Operations manager and Dispatcher land on the Dashboard. Operations manager and Dispatcher see this Operations view; the Owner can switch between Business and Operations.

1. Choose a Branch and a Date, or select Today to return to the current day. Select Filters to narrow by Technician or Service category.
2. Read the key figures: jobs, Unassigned, At risk / delayed, Technician utilization and On-time arrival.
3. Review the visit list, then Needs attention for items that require follow-up.
4. Check Technician availability to see who is working and who is free.
5. Scroll to Estimated vs actual duration, Service performance and Live operational activity for trends.
6. Select Open dispatch board to assign or adjust visits, and the refresh icon to reload the data.

**What happens next?**

The links in each card open the related jobs, schedule or team page.

##### Read the Business Overview

_Follow revenue, pipeline and cash flow._

The Business Overview summarizes financial results and capacity for a chosen period.

> The Business view is available to the Owner only. Switch to it with the Business / Operations control on the Dashboard.

1. Choose a Branch and a Date range: This month, Last month, Last 30 days, This quarter, This year or Custom.
2. Turn on Compare to previous period to see changes against the previous period.
3. Read the key figures, then Revenue & estimated profit and Cash flow snapshot.
4. Review Sales pipeline, Branch performance, Service profitability estimates and Team & capacity.
5. Check Owner attention for alerts that need a decision.
6. Select Export report to download the report for the period.

**What happens next?**

Profit and profitability figures are estimates based on the data recorded in FieldOps.

##### Use the Overview page

_Know what you see without a dashboard._

Roles without a dashboard land on the Overview welcome page instead.

> Accounting and Viewer roles land on Overview. Technicians use Today's jobs, and the Owner, Operations manager and Dispatcher use the Dashboard.

1. Sign in to open Overview. It greets you with Welcome and your first name.
2. Read your Organization and Role to confirm which account you are using.
3. Use the sidebar to open the areas your role can access, such as Reports or Audit log.

**What happens next?**

Overview has no charts or figures. Open Reports for business data when your role includes it.

#### REPORTS & ACTIVITY

##### Use the Reports workspace

_Filter, compare and export business reports._

Reports lets you pick a report, filter it by period and branch, compare it and export it.

> Owner, Operations manager, Dispatcher and Accounting can open Reports, and each sees only some reports. Job profitability and Invoice aging: Owner, Operations manager, Accounting. Quote conversion, Completed jobs & on-time performance and Technician utilization: Owner, Operations manager, Dispatcher.

1. Open Reports and choose a report from the catalog. Use Search reports, Favorites or Recently viewed to find one faster.
2. Set the Date range and Branch. Turn on Compare to previous period to see the change.
3. Use the filters under the report name, then select Apply filters, or Reset to clear them.
4. Read the summary figures, the chart and the table. Sort columns and change pages in the table.
5. Select the star to add the report to Favorites.
6. Select Export CSV or Download PDF to export. Export history opens a list of earlier exports; files are not stored, so select Open report to run one again.

**What happens next?**

Invoice aging reflects today, so its date range and comparison are turned off.

##### Review the Audit log

_Trace who changed what and when._

The Audit log records changes made in your organization so you can review who did what and when.

> The Audit log is available to Owner, Operations manager and Accounting. Events are read-only and cannot be edited or deleted.

1. Select Audit log in the sidebar to open the Audit trail page.
2. Read the summary cards at the top: Events today, Users active, High-impact changes and Access changes.
3. Search by user, entity, reference or event ID, or filter by Date, User, Action, Entity, Branch and Impact, then select Apply filters.
4. Sort by Timestamp or Impact and move between pages in the table.
5. Select View on a row to open the Audit event with its change summary (Before and After) and metadata.
6. Select Export CSV to download the events.

**What happens next?**

In the event details you can copy the event ID to share it with support.

##### Follow Notifications

_Stay on top of what needs attention._

Notifications tells you about activity such as requests, quotes, visits, jobs, invoices and payments.

> Notifications are available to internal roles, but not to technicians. For technicians the bell and Notifications are Coming soon.

1. Select the bell icon to see recent notifications, or select View all notifications to open the full page.
2. Choose All notifications or Unread, then search or filter by Type and Date (and Branch when your organization has more than one).
3. Select a notification to mark it read and, when it has a related record, open that record.
4. Use the row menu to mark one as read or unread.
5. Select Mark all as read to clear every unread notification.

**What happens next?**

The bell shows the unread count. When new notifications arrive, the page offers a Refresh button.

### Account & administration

Manage your own account and the people and records of your organization.

#### YOUR ACCOUNT

##### Update your profile

_Change your name, phone and photo._

Your profile shows your name, contact details and photo. You can change them at any time from your account settings.

> Your email address, organization and role cannot be changed here. An administrator manages your role.

1. Open your name at the top right and select My profile. Technicians open More and select My profile.
2. To change your photo, select Change photo and choose a JPG or PNG file of 5 MB or less. Select Remove photo to delete it.
3. Edit your First name and Last name. Add or update your Phone number if you wish.
4. Select Save changes. Select Cancel to leave without saving.

**What happens next?**

Your updated name and photo appear across FieldOps. Your Membership details stay read-only.

##### Change your password

_Keep your account secure._

Change your password from Account settings. You need your current password to set a new one.

1. Open your name at the top right and select Account settings, then Security. Technicians open More and select Account settings.
2. Enter your Current password.
3. Enter your New password and repeat it in Confirm new password. The Password requirements list shows which rules are met.
4. Select Update password.

**What happens next?**

Changing your password keeps you signed in on this device and signs you out on other devices.

#### ADMINISTRATION

##### Manage users and permissions

_Invite users and review role access._

Users & permissions lists everyone who can sign in to your organization, with their role and branch access. Invite new people and change or suspend access here.

> Only owners can invite users and change access. Viewers can open the page but it is read-only. Other roles do not see Administration.

1. Select Administration in the sidebar, then Users & permissions.
2. Use the filters to find people by role, branch or status: Active, Pending invitation or Suspended.
3. Select Invite user.
4. Enter the Email address, First name and Last name, choose the Role and set the Branch access. For Operations Manager, Dispatcher and Technician roles you can also switch on Link operational team profile after acceptance.
5. Choose how long the invitation lasts in Invitation expires in, then select Send invitation.
6. To change someone later, select Edit access on their row. The row menu offers Suspend access and Reactivate, and for pending invitations Resend invitation and Revoke invitation.

**What happens next?**

The person receives an invitation email and accepts it to start using FieldOps. The last Owner cannot be suspended or downgraded. Roles are Owner, Operations Manager, Dispatcher, Technician, Accounting and Viewer; the Permission matrix at the bottom of the page shows what each one can do.

##### Manage technician skills and availability

_Set the skills and hours used for assignment._

Technician profiles hold the skills and working hours that help you choose who to assign. Open Team in the sidebar to work with them.

> Owners and operations managers can edit. Dispatchers see the information read-only, and technicians see only their own profile. The Skills and Availability tabs and Manage skills buttons on the Team page are not available yet; use the profile steps below.

1. To create a profile, select Add technician profile on the Team page, fill in the details and save.
2. Open a technician from the list (View in the row menu) to see the profile drawer, then select Edit skills or Edit availability. Both open the Skills & availability page.
3. In Operational skills, search and add skills, set the level, and select Set as primary for the main one. Use Manage skill list to add skills to your organization.
4. In Standard weekly availability, set the working days, start and end times, breaks and capacity. Copy Monday to weekdays or Reset can speed this up.
5. In Availability exceptions, select Add exception to override the weekly schedule for a date.
6. Select Save changes.

**What happens next?**

Dispatchers can compare technicians by skills and availability when assigning work. FieldOps never assigns a technician automatically. The Overview, Schedule and Job history tabs on this page are not available yet.

##### Manage customer records

_Find, edit, import and archive customers._

Customers keeps every customer, their properties and notes in one place. Search, add or import customers and archive the ones you no longer work with.

> Owners and dispatchers can add and edit customers; only owners can import. Other roles see them read-only. On a customer page, the Requests, Quotes, Jobs and Invoices tabs only show a coming soon message; Overview and Activity work.

1. Select Customers in the sidebar. Use the All customers, Leads, Active and Archived tabs and the search and filters to find a customer.
2. Select New customer to add one. If FieldOps finds a Possible existing customer, review it and choose View existing customer or Create anyway.
3. To add many at once, select Import customers, download the CSV template, drop your file and review any row errors or duplicate warnings before importing.
4. Open the row menu to View, Edit, Archive or Reactivate a customer. Select a name to open its page.
5. On the customer page, use Edit customer for details. The properties card has Add property, Edit, Set as primary and Archive, and Internal notes lets you add notes.

**What happens next?**

Archived customers move to the Archived tab and can be reactivated. Their history is kept.

##### Invite a customer to the portal

_Give a customer access to their portal._

The customer portal lets a customer sign in to see their own information. Invite the customer from their page and they activate access from an email.

> Owners and dispatchers can invite. If the contact cannot receive an invitation, FieldOps shows a message and nothing is sent.

1. Select Customers and open the customer.
2. In the Contact details card, check the email address, then select Invite to portal (shown while Portal access is Not invited).
3. The card shows Portal access as Invitation sent with its expiry date. Select Resend invitation to send it again.
4. To stop access, select Remove portal access and confirm. The customer is signed out of the portal.

**What happens next?**

The customer receives an email that opens the portal activation page, confirms their name, sets a password (or enters their current one if they already have an account) and selects Activate access. Portal access then shows as Active since the activation date.

## Help for customers (`/portal/help`)

### Popular questions

**How do I request a service?**

On your portal Home, select Request a service, describe the work you need and send the request. You can follow its progress under Requests.

**How do I approve a quote?**

Open the quote from Quotes or select Review quote on your Home, check the price and select Approve quote.

**How do I pay an invoice?**

Open the invoice from Invoices and choose to pay online, then pay securely with your preferred payment method.

**How do I reschedule an appointment?**

Open the appointment from Appointments, select Request reschedule and tell us the date or time you prefer.

### Your customer portal

Access your portal and keep your service locations up to date.

#### GET STARTED

##### Activate your portal access

_Accept your invitation and create your password._

Your service company invites you to the customer portal by email. Use the invitation link to activate your access once.

> If the page says Invitation unavailable, the link can no longer be used. Contact your service company to ask for a new invitation.

1. Open the invitation email and select its link to open the activation page.
2. If the page asks for your Last name, enter it. It only appears when your name is incomplete.
3. Enter a Password and repeat it in Confirm password. Use 12 to 128 characters that differ from your email.
4. If you already have a portal password from another company, the page asks for your Current password instead. Enter it and select Link my account.
5. Otherwise, select Activate access.

**What happens next?**

Your access is ready. From then on, use Sign in with your email and password to open your portal.

##### Sign in and reset your password

_Access your portal or recover your access._

Sign in with the email and password you created when you activated your access. If you forget your password, you can reset it by email.

> The portal has no page for changing your password while signed in. To choose a new one, use Forgot password? on the sign-in page.

1. Open the sign-in page of your portal, titled Sign in to your portal.
2. Enter your Email and Password. Select Show password if you want to check what you typed.
3. Select Remember me if you want to stay signed in on this device. Skip it on a shared computer.
4. Select Sign in.
5. If you forgot your password, select Forgot password?, enter your email and select Send reset link. Open the email, select the link, then choose a new password, confirm it and select Reset password. The page then says Password updated; select Continue to sign in.

**What happens next?**

After signing in you land on Home. The reset link can be used once and expires in 30 minutes; if it expires, request a new link from the same page.

##### Get around your portal

_Find requests, appointments, quotes and invoices._

The top bar of the portal gives you access to every area. Home summarizes everything that needs your attention.

> On a small screen the sections are in a menu. Select Open navigation at the top left to see them.

1. Use Home, Requests, Appointments, Quotes and Invoices in the top bar to move between areas.
2. On Home, review the cards for Action required, Payment due, Property, Upcoming appointment, Active requests, Messages & updates and Recent activity. Select Request a service to ask for new work.
3. Select Help in the top bar to open the Help & support center. To reach your service company, use the Need help? card on Home: Call shows when the company has a phone number and Send a message when it can receive messages.
4. Select the bell to open your updates. A number on it shows unread updates.
5. Select your name to open the account menu. If you belong to more than one customer account, choose one under Switch account. Select Sign out when you finish.

**What happens next?**

Use Home as your starting point each time you sign in. Anything that needs you, such as a quote to review or an invoice to pay, appears there first.

#### YOUR DETAILS

##### Manage your properties

_Keep your service locations up to date._

Properties are the service locations on your account. You can add new ones and update their name and access details.

> You cannot change an address after a property is created. The page tells you to contact your service company to change it.

1. On the Your property card on Home, select Manage to open Properties and review your service locations.
2. To add one, select Add property (on Properties or on the Your property card).
3. Enter a Property name you will recognize and the service address. Add Access instructions if the crew needs them, such as a gate code or where to park.
4. Select Add property to save it.
5. To update an existing property, select Edit on its card, change the name or access instructions and select Save changes.

**What happens next?**

The property appears in your list and you can pick it when you request a service. Use the Property filter on Home, Requests and Appointments to focus on one location.

##### Follow your updates and activity

_See what changed and what you need to do._

Messages & updates is a feed of what changed on your account, such as quotes, appointments and invoices. Recent activity lists your completed services.

> Messages & updates is a read-only feed, not a chat. To reach your service company, use the phone number or Send a message on the Need help? card on Home when they are shown.

1. Select the bell, or View all on the Messages & updates card on Home, to open the feed.
2. Use the filters All, Action required, Appointments and Billing to narrow the list, and turn on Unread only to see what you have not opened.
3. Type in Search updates to find a specific update.
4. Select the button on an update to open the related quote, appointment or invoice.
5. To review past work, select the Recent activity card on Home. The table shows each service with its date, reference, amount and status, and lets you open an invoice, a report or a receipt when one is available.

**What happens next?**

Unread updates are marked in the feed and counted on the bell. When nothing is unread, the feed says You are all caught up.

### Requests & appointments

Request work, follow its progress and manage your appointments.

#### REQUESTS

##### Request a service

_Tell us what you need and when._

Send a service request from your portal in four short steps. Your contact details are already known, so there is no contact form.

> Submitting a request does not confirm a price or an appointment. Your service company reviews it first. If online requests are turned off, the page tells you to call the company instead.

1. Select Request a service on Home or on the Requests page.
2. In Property, choose one of your locations or select Add a new property and enter its address.
3. In Service details, choose the type of service, pick a Service or select the not-sure option, and describe the problem. Choose how urgent it is: Standard, Urgent or Emergency. You can add photos or documents (JPG, PNG or PDF, up to 10 MB each).
4. In Availability, choose As soon as possible, Choose a date or I'm flexible, then a time: Morning, Afternoon, Evening or Any time. Add scheduling notes if you wish.
5. In Review, check your answers (select Edit to change any), confirm they are accurate and select Submit request.

**What happens next?**

You see Your request is on its way with your request number. Your service company reviews the details and contacts you with the next steps, and changes appear in your updates.

##### Request a service without signing in

_Use the public request form._

Your service company can share a public request form. You can use it without creating an account or signing in.

> For immediate danger, shut off utilities if it is safe and contact emergency services. Do not wait for a reply to a request.

1. Open the public request form link of your service company (it ends with the company name after /request/). The form shows that you are continuing as a guest. If it says This request form is not available, the link cannot be used.
2. In Contact, enter your first and last name, Email address and Mobile phone. Choose at least one way to get updates: Email or Text message.
3. In Property, choose the kind of place and enter the address, city, state and ZIP code. Add access instructions if they help.
4. In Service details, choose the service type, describe the problem, set the urgency and add photos or documents if useful.
5. In Availability, choose when you prefer the visit and add notes if you wish.
6. In Review, check everything, confirm the details are accurate and select Submit request.

**What happens next?**

You see Request received with your request number, and the page says a confirmation will be sent to your email. Keep the number for your records. Select Submit another request if you need to send a second one.

##### Follow your requests

_Check the status of what you asked for._

The Requests page lists everything you have asked for, with its current status.

1. Select Requests in the top bar.
2. Review the table: request number, title, property, status and the date you submitted it. Use the Property filter if you have several locations.
3. Select View on a request to see its details.
4. The detail page shows the category, service, urgency, property, preferred availability and your description.
5. If a quote was prepared for the request, it appears in a Quote section with its status.

**What happens next?**

The status changes as your service company works on the request. Select Request a service on this page to send a new one.

#### APPOINTMENTS

##### View your appointments

_See upcoming and past visits._

The Appointments page shows your scheduled visits and your service history.

1. Select Appointments in the top bar.
2. Use the Upcoming and Past tabs to switch between future and earlier visits.
3. Review the date, time, service, work order, technician, property and status. A visit without a technician shows To be assigned.
4. Select View to open an appointment.
5. The detail page shows the date, the arrival window or time, the technician and the property, plus a progress tracker. When a report is available, select View report.

**What happens next?**

Your next visit also appears on Home in the Upcoming appointment card.

##### Request a reschedule

_Ask for a different date or time._

If a visit no longer suits you, ask your service company for a different date or time from the appointment.

> Your appointment does not change until your service company confirms. Request reschedule only appears while a change can still be requested.

1. Open the appointment from Appointments, or from the Upcoming appointment card on Home.
2. Select Request reschedule.
3. Choose a Preferred date and a Time window.
4. Add a Reason so your service company understands what you need.
5. Select Send request. Select Cancel to close without sending.

**What happens next?**

The appointment shows Reschedule requested with the date you asked. Your service company reviews it and contacts you.

### Quotes & invoices

Approve work and pay what you owe.

#### QUOTES

##### Review and approve a quote

_Check the price and approve the work._

When a quote is ready, you can review the proposed work and price and approve it online. Open it from the link in your email, or from Quotes in the portal menu.

> Approving a quote does not charge your payment method. A quote can only be approved until its Valid until date.

1. Open the quote from your email, or select Quotes in the portal and choose Review next to a quote marked Awaiting your approval. The Home page also shows it under Action required.
2. Check the quote number, the Sent date, the Valid until date and the Quote total. The total shows No payment due today.
3. Review the Price details. If some items are optional, use their toggles to include or leave them out; the total updates.
4. Read the Terms and next steps section.
5. Select I approve the scope of work and agree to the terms, then select Approve quote.
6. Select Download PDF if you want a copy of the quote.

**What happens next?**

Your approval is recorded and the quote shows as Approved. The company then contacts you to confirm the service date and assigned technician.

##### Ask a question or decline a quote

_Get answers or turn a quote down._

If you are not ready to approve, you can send the company a question or decline the quote. Both options are on the quote page.

> Declining cannot be undone. A quote that has expired or was replaced by a newer one can no longer be answered; contact the company if you still want the work.

1. Open the quote from your email or from Quotes in the portal.
2. To ask something, select Ask a question, write your Question and select Send question. The company will contact you soon. You can send one question per quote.
3. To turn the quote down, select Decline quote, write a Reason in the Decline this quote? window and select Decline quote again to confirm.
4. Check the result in Quotes: the status changes to Question sent or Declined.

**What happens next?**

After you send a question, the quote stays open so you can still approve it once you have your answer. After you decline, your decision is sent to the company and the quote is closed.

#### INVOICES

##### View your invoices

_Check balances, due dates and status._

Invoices lists every invoice the company has sent you, with what you still owe and when it is due.

1. Select Invoices in the portal menu. Invoices that need payment also appear under Payment due on Home, and the company emails you a link when an invoice is sent.
2. Read the table: Invoice, Service, Issued, Due, Total, Balance due and Status.
3. Check the status: Sent, Partially paid, Overdue or Paid.
4. Select View to open an invoice and see its full details.

**What happens next?**

From an open invoice you can pay it or report a bank transfer. Once it is fully paid, the invoice shows as Paid.

##### Pay an invoice online

_Pay securely from your invoice._

Open invoices include a Pay invoice panel where you choose how to pay. Card payments are processed through Stripe, and your card details are entered in its secure card form.

> Which methods you can use depends on what the company has enabled. A method that is not available is disabled, with a short reason shown below the options.

1. Open the invoice from Invoices in the portal or from the emailed link.
2. In Pay invoice, choose Credit or debit card (selected by default when available).
3. Enter the Cardholder name and your card details in the secure card form.
4. Select the Pay button, which shows the amount you owe, and keep the page open while your payment is processed.
5. If the payment fails, select Try payment again, or Choose another payment method.

**What happens next?**

When the payment is confirmed you see Payment successful with your receipt number, and you can select Download receipt or Download invoice. If the invoice has a receipt email on file, the page says a receipt was sent to that address.

##### Report a bank transfer

_Tell us you sent a transfer._

If you pay by bank transfer, send the money from your bank and then tell the company so they can look for it.

> Reporting a transfer does not mark the invoice as paid. Bank transfer must be enabled by the company; otherwise it is disabled. Bank transfers may take 1–3 business days to appear. For cash, the invoice shows how to contact the company to arrange it.

1. Open the invoice and, in Pay invoice, choose Bank transfer (ACH).
2. Use the Bank transfer instructions. Select the copy button next to a detail to copy it.
3. Use the invoice number as the payment reference.
4. Send the transfer from your bank, then select I sent the transfer.
5. If you have questions, select I have a question about this invoice when it is shown.

**What happens next?**

The invoice shows Transfer reported on the date you selected the button. The company confirms it once it arrives; until then the invoice stays open.

