# Help & support center

| Field    | Value                 |
| -------- | --------------------- |
| Feature  | `help-support-center` |
| Type     | Frontend              |
| Status   | APPROVED              |
| Created  | 2026-10-16            |
| Updated  | 2026-10-16            |
| Approved | 2026-10-16            |

## Context and objective

The top-bar help icon of the main shell opens a Coming soon page and signed-in
users have no in-product guidance. This feature adds a static Help & support
center at `/help`: a home page, four topic pages and one reusable article
template for 24 instructional articles, with client-side search, popular
questions, related navigation and an email-based Contact support action.
Success means every signed-in internal user, including technicians, can find
and read step-by-step instructions for behavior FieldOps already implements,
without any backend call or persisted data.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Signed-in internal user, any role (`owner`, `viewer`, `dispatcher`, `technician`, `accounting`, `operations_manager`, unknown code) | Open `/help`, `/help?view=all`, every topic page and every article; search; expand popular questions; give in-memory article feedback; start a support email | Create, edit or delete help content; persist searches or feedback |
| Unauthenticated visitor | — | Open `/help/**` (existing authenticated-shell guard redirects to `/auth/sign-in`) |
| System | Serve identical static content to every organization and role | Send any help request, search term or feedback to the backend; add user or organization data to the support email |

## Scope

- `/help` home page, `/help?view=all` article list, four topic pages and the
  article template at `/help/:topicSlug/:articleSlug`.
- Entry points: main-shell user menu, main-shell top-bar help icon and the
  technician shell **More** panel.
- Help search across article titles, descriptions and content, globally and
  per topic.
- Popular questions accordion, **Popular in this topic** and **Related topics**.
- Contact support as a `mailto:` action using a configured public support
  address.
- 24 complete articles stored as typed static frontend data, with optional
  per-step screenshots.
- In-memory **Was this article helpful?** Yes/No feedback.

## Non-goals

- Live chat or chatbot.
- Support-ticket management.
- Article CMS or administration panel.
- Video tutorials.
- Public or customer-portal documentation; the customer portal Help link is
  unchanged.
- Translations of help content.
- Backend persistence or analytics for searches or article feedback.
- Producing screenshots; they are supplied progressively by the user.
- Markdown content or any new dependency.

## User flow

1. A user selects **Help & support** in the user menu, the top-bar help icon
   (main shell) or **More → Help & support** (technician shell).
2. System opens `/help` inside the main shell with no sidebar item selected.
3. The user searches, opens a topic card, expands a popular question or selects
   **View all help articles**.
4. The user opens an article and follows its numbered steps.
5. System shows **What happens next?**, previous/next navigation within the
   topic and the feedback question.
6. If the user still needs help, **Contact support** opens their email client
   with a prefilled subject.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The main-shell user menu must list **My profile**, **Account settings**, **Help & support** (→ `/help`), a separator and **Sign out**, in that order; the top-bar help icon must navigate to `/help`; `/coming-soon/help` must redirect to `/help`. |
| FR-02 | The technician shell **More** panel must list **My profile** (unchanged), **Account settings** (unchanged), **Help & support** (→ `/help`, rendered in the main shell) and **Sign out** (unchanged), in that order. |
| FR-03 | `/help` must show breadcrumb **Help**, title **Help & support**, subtitle **Find answers and learn how to use FieldOps.**, a search field **Search help articles...**, **Browse by topic** with the four topic cards (BR-03), **Popular questions** (BR-05) and a **Still need help?** card with **Contact support**, **View all help articles** and **Support is provided by email.** |
| FR-04 | Help search on `/help` must filter all 24 articles by title, description and article content per BR-06 and show a result list in place of **Browse by topic**; each result shows article title, description and topic title and opens the article. |
| FR-05 | `/help?view=all` must show heading **All help articles**, a **Back to Help & support** link and all 24 articles grouped by topic in catalog order, using the result-list presentation of FR-04. |
| FR-06 | **Popular questions** must be an accordion in which several questions may be open at once; each open question shows a short answer and a **Read the full article** link to its article. |
| FR-07 | Each topic page must show breadcrumb **Help / {Topic}**, **Back to Help & support**, the topic icon, title and header subtitle, a search field **Search in {Topic}...**, **Articles** grouped by section, **Popular in this topic** (three numbered entries with title and description), the **Still need help?** card with **Contact support**, and **Related topics** with the other three topics in catalog order. |
| FR-08 | The topic search must filter only that topic's articles per BR-06, hiding sections without matches and showing the no-match state of BR-07. |
| FR-09 | Every article must render through one template: breadcrumb **Help / {Topic} / {Article}**, a **Back to {Topic}** link, title, introduction, estimated reading time (BR-08), an optional information callout, numbered instructions with an optional screenshot per step, a **What happens next?** section, previous/next navigation within the topic (BR-09), the feedback question (FR-10) and the **Still need help?** card. |
| FR-10 | **Was this article helpful?** must offer **Yes** and **No**; selecting either replaces the question with **Thanks for your feedback.**; the state is in memory only and resets on article change or reload. |
| FR-11 | **Contact support** must open a `mailto:` link to the configured support address with subject **FieldOps support request**, or **FieldOps support request – {Article title}** from an article, and no body. |
| FR-12 | An unknown topic slug or an unknown/mismatched article slug must show **Article not found** with a **Back to Help & support** link. |
| FR-13 | No main-sidebar item may appear selected on any `/help/**` page. |
| FR-14 | The help catalog must contain exactly the topics, sections, articles, popular entries and popular questions of BR-03 to BR-05, each article with complete content per BR-10. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Access: every route under `/help` is a child of the authenticated main shell, with the same guard and role rule as `/account` (all internal roles, including `technician` and unknown codes). | Frontend |
| BR-02 | Support address: a public, non-secret `supportEmail` setting in every frontend environment file, provisionally `support@fieldops.app` in all environments. | Frontend |
| BR-03 | Topics, in catalog order, with slug, card subtitle (home and Related topics) and header subtitle (topic page): see **Help catalog → Topics**. | Frontend |
| BR-04 | Articles per topic, sections and order, and **Popular in this topic** entries: see **Help catalog → Articles**. Article slugs are the kebab-case form of the article title and are unique within their topic. | Frontend |
| BR-05 | Popular questions on `/help`, in order: **How do I create a service request?** → Create a service request; **How do I turn an approved quote into a job?** → Create a job from a quote; **How do I assign a technician?** → Assign a technician; **How do I record a payment?** → Record a payment. Each answer is 1–2 sentences. | Frontend |
| BR-06 | Search: the query is trimmed; under 2 characters no filter applies and the default view shows. Matching ignores case and diacritics; every whitespace-separated term must appear in the article's title, description, introduction, callout, step text or **What happens next?** text. Results keep catalog order (topic, then article). | Frontend |
| BR-07 | No-match states: `/help` shows **No articles match "{query}"** followed by the **Still need help?** card; a topic page shows **No articles in {Topic} match "{query}"**. Clearing the query restores the default view. | Frontend |
| BR-08 | Reading time = words of introduction, callout, steps and **What happens next?** ÷ 200, rounded up, minimum 1; shown as **{n} min read**. | Frontend |
| BR-09 | Previous/next follow the article order of the same topic only; the first article has no previous link and the last has no next link. Each link shows the target article title. | Frontend |
| BR-10 | Content: every article has a title, description, introduction, at least one numbered step and **What happens next?**. Content describes only behavior FieldOps implements at implementation time; it never describes controls or operations that do not exist. Where a designed article's operation is only partly available, the article states the current limit and points to Contact support. | Frontend |
| BR-11 | Screenshots are optional per step; when present they require non-empty alternative text and are served as static frontend assets. Absent screenshots render nothing and never block an article. | Frontend |
| BR-12 | Copy for titles, subtitles, section labels, card text and buttons follows the approved designs exactly. | Frontend |

## Help catalog

### Topics

| Order | Slug | Title | Card subtitle (home, Related topics) | Header subtitle (topic page) |
| ----- | ---- | ----- | ------------------------------------ | ---------------------------- |
| 1 | `getting-started` | Getting started | Set up your workspace and learn the basics. | Set up your workspace and learn the FieldOps basics. |
| 2 | `requests-quotes-jobs` | Requests, quotes & jobs | Manage work from the first request to completion. | Manage customer work from the first request to completion. |
| 3 | `scheduling-team` | Scheduling & team | Assign visits and coordinate your field team. | Plan visits and coordinate your field team. |
| 4 | `invoices-payments` | Invoices & payments | Create invoices and track customer payments. | Create invoices and keep customer balances up to date. |

### Articles

| Topic | Section | Article (description) | Popular in this topic |
| ----- | ------- | --------------------- | --------------------- |
| Getting started | SET UP YOUR WORKSPACE | FieldOps basics (Understand the main areas and workflow.) | — |
| Getting started | SET UP YOUR WORKSPACE | Set up your organization (Review your company details and defaults.) | — |
| Getting started | SET UP YOUR WORKSPACE | Create and manage branches (Organize work by service location.) | 3 |
| Getting started | FIRST STEPS | Invite your team (Add staff and assign the right role.) | 2 |
| Getting started | FIRST STEPS | Add your first customer (Create a customer and service property.) | 1 |
| Getting started | FIRST STEPS | Configure products and services (Prepare reusable items for quotes and invoices.) | — |
| Requests, quotes & jobs | REQUESTS & QUOTES | Create a service request (Record a new customer request and its details.) | — |
| Requests, quotes & jobs | REQUESTS & QUOTES | Build and send a quote (Add services, pricing and send it for approval.) | 2 |
| Requests, quotes & jobs | REQUESTS & QUOTES | Record a quote response (Capture an approval, rejection or clarification.) | — |
| Requests, quotes & jobs | JOBS & VISITS | Create a job from a quote (Turn approved work into an active job.) | 1 |
| Requests, quotes & jobs | JOBS & VISITS | Add the first visit (Choose the date, time and work location.) | — |
| Requests, quotes & jobs | JOBS & VISITS | Complete a job (Review finished work and prepare it for billing.) | 3 |
| Scheduling & team | SCHEDULING | Schedule a visit (Set the date, time and expected duration.) | 2 |
| Scheduling & team | SCHEDULING | Assign a technician (Match the visit with an available team member.) | 1 |
| Scheduling & team | SCHEDULING | Reschedule or cancel a visit (Update the plan while keeping its history.) | — |
| Scheduling & team | FIELD WORK | Update a visit status (Move a visit from scheduled to completed.) | 3 |
| Scheduling & team | FIELD WORK | Track time on a visit (Start, pause and finish technician time entries.) | — |
| Scheduling & team | FIELD WORK | Use the schedule calendar (Review team workload by day or week.) | — |
| Invoices & payments | INVOICING | Create an invoice (Generate an invoice from completed work.) | 1 |
| Invoices & payments | INVOICING | Review and send an invoice (Confirm charges before sending it to the customer.) | — |
| Invoices & payments | INVOICING | Report a bank transfer (Record transfer details awaiting review.) | — |
| Invoices & payments | PAYMENTS | Record a payment (Apply a received payment to an invoice.) | 2 |
| Invoices & payments | PAYMENTS | Review overdue invoices (Find balances that need follow-up.) | 3 |
| Invoices & payments | PAYMENTS | Void an invoice (Cancel an invoice without deleting its history.) | — |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Feedback question | **Thanks for your feedback.** | Selects Yes or No | Any user | Same article view |
| **Thanks for your feedback.** | Feedback question | Opens another article or reloads | Any user | — |
| Default view | Search results / no-match | Query reaches 2 characters | Any user | BR-06 |
| Search results / no-match | Default view | Query cleared or under 2 characters | Any user | — |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`): None.
- Schema amendments required: None.
- Content is typed static TypeScript data in the frontend; no new dependency.

## Tenant isolation and authorization

- Organization context: not used; help content contains no organization data
  and is identical for every organization and role.
- Client-provided organization identifiers: none; the feature sends no request.
- Resource of another organization: not applicable.
- Access: authenticated-shell guard only (BR-01); frontend guards are UX, and
  no backend data is exposed by this feature.

## API contracts

Contract status: Final — no API calls.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/help` | N/A (static) | Search no-match (BR-07) | N/A | Unauthenticated → sign-in | Browse view, search results or All help articles |
| Topic page | N/A | Topic search no-match (BR-07) | Unknown topic → **Article not found** (FR-12) | Unauthenticated → sign-in | Articles, popular, related topics |
| Article | N/A | N/A | Unknown article → **Article not found** (FR-12) | Unauthenticated → sign-in | Article template (FR-09) |

- Mockups (Approved): `design/assets/design-33.png` (home),
  `design/assets/Ayuda de FieldOps_ primeros pasos-1.png`,
  `design/assets/Ayuda FieldOps_ solicitudes, presupuestos y trabajos-2.png`,
  `design/assets/Ayuda de programación y equipo-3.png`,
  `design/assets/Ayuda de facturación y pagos-4.png` (topic pages).
- No mockup for the article page, search results or **All help articles**: by
  decision they follow the topic-page visual language (breadcrumb, back link,
  cards, icon tiles, **Still need help?** card) and are validated by user
  visual QA.
- Where the topic designs differ in **Popular in this topic** or **Still need
  help?** layout, every topic uses the same layout: numbered entries with title
  and description.
- Responsive: mobile-first; topic cards one column on narrow screens and two
  from the desktop breakpoint; topic-page side column stacks below **Articles**
  on narrow screens; Related topics stack to one column.
- Accessibility: breadcrumbs in a labelled navigation landmark with the current
  page marked; search fields have accessible names; result counts are announced
  politely; accordion headers are buttons exposing expanded state; steps are an
  ordered list; screenshots carry alt text; feedback buttons are reachable by
  keyboard and the confirmation is announced.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Unknown topic slug or article slug, or article under the wrong topic | **Article not found** + **Back to Help & support** | None |
| Search with no matches | BR-07 message | None |
| User has no email client | Browser default `mailto:` handling; no in-app message | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A signed-in user in the main shell (parameterized: `owner`, `dispatcher`, `technician`) | They open the user menu | It lists **My profile**, **Account settings**, **Help & support**, a separator and **Sign out** in that order; **Help & support** navigates to `/help` |
| AC-02 | A signed-in user in the main shell | They select the top-bar help icon, or open `/coming-soon/help` | The browser ends on `/help` |
| AC-03 | A signed-in `technician` in the technician shell | They open **More** and select **Help & support** | The panel lists My profile, Account settings, Help & support, Sign out in that order; `/help` renders in the main shell |
| AC-04 | An unauthenticated visitor | They open `/help` or any `/help/**` URL | They are redirected to `/auth/sign-in` |
| AC-05 | A signed-in user | They open `/help` | Breadcrumb, title, subtitle, search, the four topic cards (BR-03 order and card copy, each linking to its topic), Popular questions and the Still need help card render per FR-03, and no sidebar item is selected |
| AC-06 | `/help` | The user types queries (table-driven: a title term, a description term, a term only in step text, mixed case, missing diacritics, two terms both present, two terms with one absent, 1 character) | Results follow BR-06: matching articles in catalog order with title, description and topic; the 1-character and one-absent cases show the default view and BR-07 no-match message respectively |
| AC-07 | A search with no matches on `/help` | The user clears the field | **Browse by topic** returns and the no-match message disappears |
| AC-08 | `/help` | The user selects **View all help articles** | `/help?view=all` shows **All help articles**, **Back to Help & support** and all 24 articles grouped by topic in catalog order |
| AC-09 | Popular questions | The user expands two questions | Both stay open, each shows its answer and **Read the full article**, which opens the BR-05 target article |
| AC-10 | Each topic page (parameterized by the four slugs) | It opens | Breadcrumb **Help / {Topic}**, back link, header subtitle, topic search placeholder, sections and articles in BR-04 order, three **Popular in this topic** entries, Still need help card and Related topics with the other three topics render; no sidebar item is selected |
| AC-11 | A topic page | The user searches a term matching one article of that topic and one of another topic | Only the in-topic article shows, sections without matches are hidden; a non-matching query shows **No articles in {Topic} match "{query}"** |
| AC-12 | Any article | It opens | The template renders breadcrumb **Help / {Topic} / {Article}** (Help and topic linked), **Back to {Topic}**, title, introduction, **{n} min read** per BR-08, numbered steps and **What happens next?** |
| AC-13 | Articles with and without a callout, and steps with and without a screenshot | They render | The callout and screenshots render only when present; present screenshots carry their alt text; absent ones leave no placeholder |
| AC-14 | The first, a middle and the last article of a topic | They render | Previous/next follow BR-09: first has only next, last has only previous, links show target titles and never cross topics |
| AC-15 | An article | The user selects **Yes** (or **No**) | **Thanks for your feedback.** replaces the question; no network request is sent; opening the next article shows the question again |
| AC-16 | `/help`, a topic page and an article | The user inspects **Contact support** | It is `mailto:support@fieldops.app` with subject **FieldOps support request** (home/topic) or **FieldOps support request – {Article title}** (article) and contains no user or organization data |
| AC-17 | Unknown topic slug, unknown article slug, and a valid article slug under another topic | The URL opens | **Article not found** and **Back to Help & support** show |
| AC-18 | The help catalog | It is inspected | It contains 4 topics and 24 articles exactly per BR-03/BR-04, unique slugs per topic, valid popular and question references, complete required fields per BR-10, and non-empty alt text for every screenshot |
| AC-19 | The 24 articles | The user reviews them against the running application | Every instruction matches implemented behavior and names only existing screens and controls (BR-10) |
| AC-20 | Desktop and narrow (mobile) widths | The user compares `/help`, the four topic pages and a representative article with the designs | Layout, copy and responsive stacking match the approved designs and the article-template rule; keyboard focus order and accordion/feedback operation work |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Frontend unit (pure) | Catalog integrity: counts, order, unique slugs, references, required fields, screenshot alt text, reading-time calculation | AC-12 (reading time), AC-18 |
| Frontend unit (pure) | Search normalization and matching, table-driven, global and topic-scoped | AC-06, AC-11 |
| Frontend component | Help home: results/no-match/clear, `view=all`, multi-open accordion and links, mailto subject | AC-05, AC-07, AC-08, AC-09, AC-16 |
| Frontend component | Article and topic routing: template with optional parts, previous/next boundaries, feedback reset without HTTP, not-found cases, no active sidebar item | AC-10, AC-12–AC-15, AC-17 |
| Frontend component (shell, existing specs updated) | User-menu order, help icon target, technician More order, `/coming-soon/help` redirect | AC-01–AC-03 |
| Authorization | Reuse existing authenticated-shell guard evidence; `/help` is a child of that shell | AC-04 |
| User visual QA | Design fidelity, article template, responsive, keyboard; content accuracy review | AC-19, AC-20 |

No backend tests: the feature has no backend change.

## Dependencies

- `specs/account-settings/spec.md` (AUDITED): its user-menu FR/AC-01 forbids a
  Help & support item, and its FR-04 fixes the technician **More** panel order.
  This spec supersedes both for the added **Help & support** item only.
- `specs/authenticated-app-shell/spec.md`: the help icon target changes from
  Coming soon to `/help`.
- Features described by articles (requests, quotes, jobs, scheduling, team,
  invoices, payments) must be implemented as they exist on `main`.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Technicians open `/help` inside the main shell, as they do for `/account`. |
| AS-02 | `support@fieldops.app` is provisional and will be replaced by a verified mailbox before real deployment; it is not a secret. |
| AS-03 | Article icons in topic-page lists follow the designs; the article template shows no icon (FR-09). |
| AS-04 | Popular question answers and full article texts are written by the implementer from current application behavior and reviewed by the user (AC-19). |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Support address | Environment `supportEmail` | No | `support@fieldops.app` in all environments, provisional (2026-10-16) |
| OD-02 | Article page design | Topic-page style + visual QA / new mockup | No | Topic-page style + user visual QA (2026-10-16) |
| OD-03 | Initial content | All 24 / popular only | No | All 24, implemented behavior only, screenshots progressive (2026-10-16) |
| OD-04 | Content format | Typed TS / Markdown | No | Typed TS, no dependency (2026-10-16) |
| OD-05 | Search behavior | — | No | BR-06/BR-07 (2026-10-16) |
| OD-06 | Popular questions | — | No | Multi-open accordion with Read the full article (2026-10-16) |
| OD-07 | View all help articles | `/help?view=all` / scroll | No | `/help?view=all` (2026-10-16) |
| OD-08 | Entry points | — | No | FR-01/FR-02 order (2026-10-16) |
| OD-09 | Previous/next scope | Same topic / all topics | No | Same topic (2026-10-16) |
| OD-10 | Feedback | — | No | In memory, resets on article change/reload (2026-10-16) |
| OD-11 | Contact support | — | No | `mailto:` with FR-11 subjects, no user/org data (2026-10-16) |
| OD-12 | Unknown slugs | Not-found state / redirect | No | **Article not found** + back link (2026-10-16) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03 |
| FR-03 | AC-04, AC-05, AC-20 |
| FR-04 | AC-06, AC-07 |
| FR-05 | AC-08 |
| FR-06 | AC-09 |
| FR-07 | AC-10, AC-20 |
| FR-08 | AC-11 |
| FR-09 | AC-12, AC-13, AC-14, AC-20 |
| FR-10 | AC-15 |
| FR-11 | AC-16 |
| FR-12 | AC-17 |
| FR-13 | AC-05, AC-10 |
| FR-14 | AC-18, AC-19 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-16 | — → DRAFT     | Created |
| 2026-10-16 | DRAFT → APPROVED | Approved by user via /spec approve |
