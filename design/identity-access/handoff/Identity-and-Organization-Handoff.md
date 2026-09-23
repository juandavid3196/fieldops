# FieldOps — Identity & Organization module · Implementation handoff

Designs: **17** Company setup · **18** Users & permissions · **26** Sign in
Reference files: `Company Setup.dc.html`, `Users and Permissions.dc.html`, `Sign In.dc.html`
Target: Angular 20 + PrimeNG (Aura preset, token-customized). No production code in this document.

**Legend** — **[C]** Confirmed design decision (in mockup or FieldOps spec) · **[S]** Suggested behavior (designer proposal, safe default) · **[O]** Open product decision (needs PO/functional sign-off)

---

## 0. Module overview

| Screen | Route (S) | Feature | Shell | Primary role |
|---|---|---|---|---|
| Sign in | `/auth/sign-in` | `authentication` | Public split-screen, no sidebar | Any user with an account |
| Company setup | `/admin/company` | `organization-onboarding` | Authenticated app shell | Owner |
| Users & permissions | `/admin/users` | `roles-and-permissions` | Authenticated app shell | Owner |

- [C] One Angular frontend. Access to each route is controlled by auth, role, and branch access.
- [S] Guards: `authGuard` on `/admin/**`, `roleGuard(['Owner'])` for edit mode, and `guestGuard` on `/auth/**`, which redirects signed-in users to Overview.
- [C] Viewer reuses the same screens in read-only mode. There is no separate module.

---

## 1. Sign in (design 26)

### 1.1 Purpose
[C] Single email + password authentication for all roles. It is also the entry to the other public flows: invitation, company registration, and guest service request.

### 1.2 Role & entry point
- [C] Any registered user: Owner, Dispatcher, Technician, Accounting, Operations Manager, Customer, Viewer.
- [S] Entry points: app root while unauthenticated, session expiry redirect (`?returnUrl=`), links in marketing site and transactional emails.

### 1.3 Navigation
| From this screen | To |
|---|---|
| Sign in success | [S] `returnUrl` if present and authorized; otherwise the role's default landing (Owner → Overview, Technician → My visits, Customer → Customer dashboard) |
| Forgot password? | `/auth/forgot-password` |
| Accept an invitation | `/auth/invitation` (token-based) |
| Create a company account | `/auth/register-company` (Owner) |
| Request a service (header button and list row) | `/request-service` (public guest form) |
| Privacy / Terms / Help | External or static pages |

### 1.4 Layout & responsive
- [C] Split screen. The left brand panel is about 55% (navy + photo) and the right form panel about 45%. The form is centered with max-width 440px.
- [C] Left panel content: logo (mark + "Field" white / "Ops" teal), tagline "People · Work · Customers · For a brighter tomorrow" (uppercase metadata style), headline, subhead, 3 status cards, 3 benefit rows, and the background photo anchored at the bottom center.
- [C] **≥1024px:** both panels visible. The status-card grid is `auto-fit, minmax(196px, 1fr)` and wraps when narrow. Headline is fluid, 34–46px.
- [S] **768–1023px (tablet):** left panel hidden. Compact logo in the top-left of the form panel. Form stays centered.
- [S] **<768px (mobile):** single column. Header keeps "Request a service" as a 44px-high target and "Need service?" may be hidden. Inputs and buttons go full width at 48px height. Footer wraps.

### 1.5 Fields, controls, actions
| Element | Label | Control | Rules |
|---|---|---|---|
| `email` | Email | `p-inputtext` type=email, `autocomplete="email"` | [C] Required. [S] Trim, lowercase, RFC-lite format |
| `password` | Password | `p-password` (`toggleMask`, `feedback=false`), `autocomplete="current-password"` | [C] Required. [C] Show/hide toggle |
| `rememberMe` | Remember me | `p-checkbox` (binary) | [S] Default unchecked in production (the mockup shows checked). Extends refresh-token lifetime |
| Forgot password? | — | Link | [C] |
| Sign in | Sign in | `p-button` type=submit, full width, 48px | [C] Primary |
| Other ways to get started | — | List of 3 routed rows (icon, title, subtitle, chevron) | [C] |
| Guest info note | "Guest requests don't create an account. You can accept an invitation later." | Inline note | [C] |
| Language | English | `p-select` (compact, text style) | [O] Supported locales |

- [C] No role selector and no social login.

### 1.6 States
| State | Behavior |
|---|---|
| Default | [C] Empty fields. [S] Email autofocus on desktop only |
| Validation error | [C] Validation runs on submit and on blur after the first submit. Invalid fields get a danger border, `aria-invalid="true"`, and a message under the field with an icon: "Enter your email address." / "Enter a valid email address, for example name@company.com." / "Enter your password." |
| Submitting | [C] Button disabled with spinner and label "Signing in…". [S] Inputs stay editable but submit is blocked |
| Invalid credentials | [C] `p-message severity="error"` above the form: "The email or password is incorrect. Check your details and try again." [S] Clear the password and focus it. Never reveal which field is wrong |
| Account locked / suspended | [O] Message copy and whether to show a support contact |
| Unverified email | [O] Block with "Resend verification email", or allow sign in with a banner |
| Pending invitation (no password yet) | [S] Same generic error, plus a hint linking to "Accept an invitation" |
| Network / server error | [S] `p-message` error "We couldn't sign you in right now. Try again in a moment." with the button re-enabled |
| Success | [S] No toast. Navigate immediately |
| Loading / empty / permission / read-only | Not applicable |

### 1.7 Dialogs & notifications
[C] None. [S] Session-expired redirect shows `p-message severity="info"`: "Your session expired. Sign in again to continue."

### 1.8 Accessibility & keyboard
- [C] Tab order: Request a service → Email → Password → Show/hide → Remember me → Forgot password → Sign in → Other ways rows → footer links → Language.
- [C] Enter submits. The show/hide button uses `aria-label` "Show password" / "Hide password".
- [C] Errors are announced: field errors via `aria-describedby`, the auth error via `role="alert"`.
- [C] The photo is decorative on the brand panel. [S] Use `alt=""`, or keep a short descriptive alt, but not both.
- [C] Visible focus ring: 3px `rgba(15,157,154,0.24)` plus teal border.

---

## 2. Company setup (design 17)

### 2.1 Purpose
[C] The Owner maintains organization identity, branches, taxes and currency, and document numbering. It is part of `organization-onboarding`: first-time setup after company registration and ongoing edits.

### 2.2 Role & entry point
- [C] Owner: full edit. Viewer (if granted): read-only. Other roles: no access, and the Administration nav entry is hidden.
- [C] Entry: Sidebar → Administration → Company profile.
- [S] First-run redirect from Overview when required company fields are missing.

### 2.3 Navigation
- [C] Breadcrumb: Administration / Company.
- [C] The Administration sub-nav is shared with screen 18: Company profile, Branches, Business hours, Users & permissions, Taxes & currency, Document numbering, Notifications.
- [S] Company profile, Branches, Taxes & currency, and Document numbering are anchors inside this page. Users & permissions is its own route. Business hours and Notifications are future routes.
- [O] Business hours at company level: is it a separate page, or only per branch (as in the drawer)?
- [C] Clicking a branch row opens the **Branch drawer** on the right. The list position is preserved.

### 2.4 Layout & responsive
- [C] Page header: title, description, actions "Discard changes" (secondary) and "Save changes" (primary). Actions are hidden for read-only users.
- [C] Sections, in order:
  1. Company profile card
  2. Branches card (table)
  3. Taxes & currency and Document numbering side by side (grid `auto-fit, minmax(340px,1fr)`)
- [C] **≥1440px:** the drawer is docked (420px) beside the content.
- [C] **1100–1439px:** the drawer is an overlay with a navy 32% mask. Clicking the mask closes it.
- [C] **<1100px:** the Administration sub-nav is hidden.
- [S] Below 1100px, the sub-nav becomes a `p-select` or horizontal tabs at the top of the content.
- [S] **Tablet:** the sidebar collapses to 72px.
- [S] **Mobile (<768px):**
  - The sidebar becomes a `p-drawer`.
  - Forms go to 1 column.
  - The branch table becomes cards (name, Main branch tag, address, status, overflow menu).
  - The branch drawer becomes full-screen.
  - Save and Discard move to a sticky bottom bar with safe-area padding.
- [C] The main column never drops below 320px wide.

### 2.5 Fields — Company profile
| Field | Label | Control | Req. | Notes |
|---|---|---|---|---|
| `logo` | Company logo | Preview + `p-fileupload` (mode=basic, "Change logo") | — | [C] JPG, PNG, SVG, max 2 MB. [S] Client-side type/size validation, inline error |
| `legalName` | Legal business name | `p-inputtext` | [C] ✓ | |
| `displayName` | Display name | `p-inputtext` | [C] ✓ | [S] Shown in customer-facing documents |
| `website` | Website | `p-inputtext` (url) | — | [S] Accept with or without protocol |
| `businessEmail` | Business email | `p-inputtext` (email) | [C] ✓ | |
| `phone` | Phone number | `p-inputmask` or `p-inputtext` (tel) | [C] ✓ | [O] Phone format / international support |
| `taxId` | Tax ID (EIN) | `p-inputtext` | — | [S] Mask `99-9999999` for US |
| `address.street/city/state/zip` | Company address | `p-inputtext` ×3 + `p-select` (state) | [C] ✓ | One label with a 4-part group. Each input has an `aria-label` |
| `defaultCountry` | Default country | `p-select` | [C] ✓ | [O] Which countries are supported in the MVP |
| `defaultTimeZone` | Default time zone | `p-select` (filter) | [C] ✓ | |

### 2.6 Branches table
- [C] Columns: Branch (sortable, with "Main branch" tag), Address, Time zone, Team (count of technicians), Status, Actions (overflow).
- [C] Above the table: search ("Search branches…") and a status filter `p-select` (All statuses / Active / Inactive). Header count: "3 branches". Primary action "Add branch".
- [C] The selected row gets a light teal background and a 3px teal inset indicator.
- [S] Row overflow `p-menu`: Edit, Deactivate / Reactivate. The main branch cannot be deactivated (item disabled with a tooltip).
- [O] Can the main branch be changed? If yes, add "Set as main branch" to the row menu.
- [S] Pagination is only needed above 10 branches, with server-side paging.

### 2.7 Branch drawer
| Field | Label | Control | Req. | Notes |
|---|---|---|---|---|
| `name` | Branch name | `p-inputtext` | [C] ✓ | |
| `code` | Branch code | `p-inputtext` | [C] ✓ | [C] Helper: "Used in document numbering and reports." [S] Unique, uppercase, A–Z 0–9 and hyphen, max 8 |
| `phone` | Contact phone | `p-inputtext` (tel) | — | |
| `email` | Contact email | `p-inputtext` (email) | — | |
| `timeZone` | Time zone | `p-select` | [C] ✓ | |
| `serviceZips` | Service area ZIP codes | `p-inputtext` (comma separated) | — | [S] `p-autocomplete multiple` (chips) is a better fit. [O] Is the ZIP list used for request routing in the MVP? |
| `useCompanyBilling` | Use company billing settings | `p-toggleswitch` | — | [C] Helper text. [O] What fields appear when it is off (branch tax rate, prefixes)? |
| `hours[7]` | Business hours | Per day: `p-toggleswitch` (open) + 2× `p-datepicker timeOnly` | — | [C] Closed day shows "Closed". [S] Validate that the end time is after the start time |

- [C] Footer: "Deactivate branch" (danger text button, left, separated), "Cancel", "Update branch" (primary).
- [C] Info note: "Deactivating a branch prevents new records. All historical data is preserved."
- [C] Correction vs. mockup: the Details / Business hours tabs were removed because they duplicated the section. The drawer is one scrollable form.
- [S] Adding a branch uses the same drawer with title "New branch", empty fields, and primary action "Create branch".

### 2.8 Taxes & currency / Document numbering
| Field | Label | Control | Req. |
|---|---|---|---|
| `currency` | Currency | `p-select` | [C] ✓ |
| `defaultTaxRate` | Default sales tax rate | `p-inputnumber` (suffix %, 0–100, 2–3 decimals) | [C] ✓ |
| `pricesIncludeTax` | Prices include tax | `p-toggleswitch` + helper | — |
| `quotePrefix` | Quote prefix | `p-inputtext` | [C] ✓ |
| `workOrderPrefix` | Work order prefix | `p-inputtext` | [C] ✓ |
| `invoicePrefix` | Invoice prefix | `p-inputtext` | [C] ✓ |
| `nextInvoiceNumber` | Next invoice number | `p-inputnumber` (integer) | [C] ✓ |

- [C] Warning note: "Changing the currency after invoices exist requires confirmation and may affect historical records."
- [C] Links: "Manage tax rates →", "Edit sequences →". [O] Destinations are undefined; they may be out of MVP scope.
- [S] `nextInvoiceNumber` cannot be lower than the last issued number (server validation).
- [O] Only the invoice sequence is editable. Should quote and work order sequences be editable too?

### 2.9 States
| State | Behavior |
|---|---|
| Loading | [C] Branch table shows 3 `p-skeleton` rows. [S] Profile fields also use skeletons on first load |
| Empty (branches) | [S] Only possible before onboarding is complete: "No branches yet. Add your first branch to start scheduling work." + "Add branch" |
| Validation error | [S] Validation runs on blur after first interaction and on save. Save shows a `p-message` error summary at the top with links to the fields. The first invalid field gets focus |
| Error | [C] Table-level `p-message` error "We couldn't load branches…" + Retry. [S] A save failure keeps edits and shows a toast error |
| Permission denied | [S] Roles without access get a 403 page inside the shell: "You don't have access to company settings." |
| Read-only (Viewer) | [C] Info banner "You have view-only access…". All inputs disabled. Save, Discard, Add branch, and Update branch hidden. The drawer opens read-only |
| Disabled | [C] Disabled bg `#E9EEF3`, text `#9AA5B1` |
| Submitting | [S] Save shows a spinner. Inputs are locked until the response |
| Success | [C] `p-toast` success: "Company settings saved" / "{Branch} updated" |
| Dirty | [S] Save and Discard are enabled only when the form is dirty. Route-leave guard is active |

### 2.10 Dialogs & confirmations (`p-confirmdialog`)
- [S] **Discard changes:** "Discard unsaved changes?" → Discard (danger) / Keep editing.
- [C] **Change currency** when invoices exist: an explicit confirmation is required (see the warning note). [O] Exact consequence copy.
- [S] **Deactivate branch:** "Deactivate {Branch}? No new requests, quotes or work orders can be created for this branch. Existing records stay available." → Deactivate (danger) / Cancel.
- [S] **Unsaved-changes guard** on route leave and on closing a dirty drawer.

### 2.11 Accessibility
- [C] Sections use `<section aria-labelledby>` with h2. The drawer uses `role="dialog"` with a labelled title.
- [S] The overlay drawer traps focus, Esc closes it, and focus returns to the originating row. The docked drawer does not trap focus.
- [C] Rows open with click. [S] Enter/Space on the focused row. The Branch column uses `aria-sort`.
- [C] Required marker `*` plus [S] `aria-required`. Toggles use `role="switch"` via PrimeNG.

---

## 3. Users & permissions (design 18)

### 3.1 Purpose
[C] Manage sign-in access, predefined roles, and branch visibility. Invite users. Make the role permission matrix visible.
[C] It does **not** manage skills, workload, or availability; those are handled in Team.

### 3.2 Role & entry point
- [C] Owner: full. Viewer (if granted): read-only. Others: hidden.
- [C] Entry: Administration → Users & permissions.

### 3.3 Navigation
- [C] Breadcrumb: Administration / Users & permissions. Shared Administration sub-nav.
- [C] "Invite user" opens the **Invite user drawer**.
- [C] Link to **Team** inside the info note.
- [S] "Edit access" opens the same drawer in edit mode (title "Edit access", email read-only).
- [S] Linked team profile name → Team profile.

### 3.4 Layout & responsive
- [C] Header (title, description, "Invite user"), then 4 metric cards (Active users 21, Pending invitations 2, Suspended 1, Owners 1), then the Users section (note, filters, table, paginator), then the Permission matrix section (with legend).
- [C] Correction: the Users / Permission matrix tabs were removed because they duplicated the stacked sections.
- [C] Drawer behavior matches screen 17: 400px wide, docked from 1440px up, overlay below. The sub-nav is hidden below 1100px.
- [S] **Mobile:**
  - Metrics become a 2×2 grid.
  - The users table becomes cards (avatar, name, email, role tag, status, overflow menu with Edit access).
  - The matrix scrolls horizontally with a sticky Module column (true tabular data).
  - The drawer becomes full-screen with a sticky footer.

### 3.5 Filters & table
- [C] Filters:
  - Search "Search by name or email…"
  - Role `p-select` (All roles plus the 7 internal roles)
  - Branch access `p-select` (All branches plus branches)
  - Account status `p-select` (All statuses / Active / Pending invitation / Suspended)
  - "Clear filters" (text button)
- [C] Columns:
  - User (avatar initials, name, email), sortable
  - Role (neutral tag)
  - Branch access
  - Linked team profile ("Not applicable" / "Not linked" / name)
  - Status tag
  - Last active
  - Actions: "Edit access" plus an overflow menu
- [C] Server-side paginator: "Showing 1–6 of 24 users".
- [S] Overflow menu items by status:
  - Active: Suspend access
  - Pending invitation: Resend invitation / Revoke invitation
  - Suspended: Reactivate
- [C] The last Owner cannot be suspended or downgraded. [S] Those items are disabled with a tooltip.
- [O] Are Customer accounts listed here, or managed only from Customers? Mockup and spec imply internal users only.
- [O] Can more than one Owner exist? The metric "Owners 1" suggests yes.

### 3.6 Permission matrix
- [C] Rows: Overview, Customers, Requests & quotes, Work orders & schedule, Invoices & payments, Reports, Team, Company settings, Audit log.
- [C] Columns: Owner, Operations Manager, Dispatcher, Technician, Accounting, Viewer.
- [C] Levels and colors:

  | Levels | Color |
  |---|---|
  | Full, Edit | Teal tint |
  | View | Info tint |
  | Scoped: Assigned only, Own profile, Completed only, Financial only | Warning tint |
  | None | Neutral |
  | View if granted (Viewer) | Warning tint |

- [C] Correction: "None" is neutral instead of red, and a legend was added so color is not the only indicator.
- [C] The matrix is read-only (predefined roles). No custom roles.
- [O] The matrix values need functional-spec confirmation. They were taken from the mockup, with corrected module names.

### 3.7 Invite user drawer
| Field | Label | Control | Req. | Notes |
|---|---|---|---|---|
| `email` | Email address | `p-inputtext` (email) | [C] ✓ | [S] Error if already a user or already invited |
| `firstName` / `lastName` | First name / Last name | `p-inputtext` ×2 | [C] ✓ | |
| `role` | Role | `p-select` | [C] ✓ | [C] Updates the role summary below |
| `branchIds` | Branch access | `p-checkbox` list + "All branches" | [C] ✓ (≥1) | [C] "All branches" selects all; unchecking one unchecks "All branches". [S] Owner and Operations Manager force All branches and disable the checkboxes |
| — | {Role} role summary | Static panel | — | [C] Copy per role |
| `linkTeamProfile` | Link operational team profile after acceptance | `p-toggleswitch` | — | [S] Only visible for roles that have a team profile (Technician, Dispatcher, Operations Manager) |
| `expiresIn` | Invitation expires in | `p-select` (e.g. 3, 7, 14 days) | — | [S] Default 7 days |
| — | Safety notes | `p-message` info (list) | — | [C] |

- [C] Footer: Cancel / Send invitation (primary).
- [C] Correction: the "Send invitation email" toggle was removed because an invitation must be delivered by email.

### 3.8 States
| State | Behavior |
|---|---|
| Loading | [C] 6 skeleton rows (avatar + bars). [S] Metric cards also use skeletons |
| Empty (filtered) | [S] "No users match these filters." + Clear filters |
| Empty (initial) | Not possible, because the current Owner always exists |
| Error | [C] `p-message` error + Retry replaces the table |
| Validation | [S] Invite drawer validates on blur and submit. Messages: "Enter a valid email address.", "This person already has access.", "Select at least one branch." |
| Submitting | [S] Send invitation shows a spinner, drawer locked |
| Success | [C] Toast "Invitation sent to {email}". The drawer closes. [S] The table refreshes and the Pending invitations metric increases by 1 |
| Read-only (Viewer) | [C] Banner. Invite button hidden, drawer disabled. The Actions column shows "View only" |
| Permission denied | [S] Same 403 pattern as screen 17 |
| Partial data | [S] If the metrics fail but the table loads, the metrics show "—" with a tooltip "Couldn't load" |

### 3.9 Dialogs & confirmations
- [S] **Suspend access:** "Suspend {name}? They won't be able to sign in. Assigned work and audit history are preserved." → Suspend (danger).
- [S] **Revoke invitation:** "Revoke the invitation for {email}? The link will stop working." → Revoke (danger).
- [S] **Change role** (Edit access) when it reduces access: confirmation with the old and new role.
- [S] Resend invitation needs no confirmation. Toast "Invitation resent to {email}".

### 3.10 Accessibility
- [C] Metric cards show icon + label + number, so color is not the only indicator. Status tags include an icon.
- [C] The overflow buttons use `aria-label` "More actions for {name}".
- [C] The matrix uses `<th scope="row">` for modules and `<th scope="col">` for roles.
- [S] Custom checkboxes in the prototype map to `p-checkbox` with a proper `<label for>`.

---

## 4. Shared components (app shell + patterns)

| Component (S name) | Used in | Notes |
|---|---|---|
| `AppShellComponent` | 17, 18 | Sidebar + top bar + router outlet |
| `SidebarNav` | 17, 18 | [C] 256px, navy. Groups: Overview · Operations (Requests, Quotes, Work orders, Schedule) · People (Customers, Team) · Finance (Products and services, Invoices, Reports) · Administration. Active item: teal 20% bg + 3px teal inset. Collapse to 72px. Hides entries the user cannot access |
| `TopBar` | 17, 18 | [C] Global search, organization selector, Help, Notifications (badge), user menu (avatar, name, role) |
| `AdminSubnav` | 17, 18 | [C] 224px secondary nav |
| `PageHeader` | 17, 18 | [C] Breadcrumb, h1, description, action slot |
| `SectionCard` | 17, 18 | [C] White, 1px border, radius 10, padding 20/24 |
| `StatusTag` | 17, 18 | [C] `p-tag` rounded + icon. Maps status to severity |
| `RecordDrawer` | 17, 18 | [C] Header (title, tag, close), scroll body, sticky footer. Docked ≥1440px, overlay below |
| `ReadOnlyBanner` | 17, 18 | [C] Info `p-message` |
| `TableErrorState` / `TableSkeleton` / `EmptyState` | 17, 18 | [C] |
| `BrandLogo` | 17, 18, 26 | [C] Mountain mark + "Field" + "Ops" wordmark (light and dark variants). The prototype app shell still uses a placeholder gear icon and must use `BrandLogo` |
| `FormField` wrapper | all | [C] Label above, required `*`, helper, error message with icon |

---

## 5. Design tokens (map to the Aura preset)

| Token | Value | Use |
|---|---|---|
| `primary.500` / `primary.600` | `#0F9D9A` / `#0B8583` | Primary buttons, active, links (600 for text) |
| `primary.50` | `#E7F6F5` | Selected row, active sub-nav, avatar bg |
| `focus.ring` | `0 0 0 3px rgba(15,157,154,0.24)` | All focusable controls |
| `navy.900` / `navy.950` | `#102A43` / `#0B2239` | Sidebar, auth panel, headings |
| `surface.ground` | `#F4F7FA` | App background |
| `surface.0` / `surface.50` | `#FFFFFF` / `#F8FAFC` | Cards / table header, secondary surface |
| `text.color` / `text.secondary` / `text.muted` | `#172B4D` / `#52606D` / `#7B8794` | |
| `border` / `border.strong` | `#D9E2EC` / `#BCCCDC` | Inputs, cards / secondary buttons, switches off |
| `disabled.bg` / `disabled.text` | `#E9EEF3` / `#9AA5B1` | Also skeleton color |
| `success` | `#168A5B` on `#E7F6EE` (text `#136F4A`) | Active, Approved, Paid |
| `info` | `#247BA0` on `#EAF4F8` (text `#1D6485`) | View level, info notes |
| `warning` | `#C47A12` on `#FFF4DD` (text `#8A5509`) | Pending invitation, scoped access, currency warning |
| `danger` | `#C93C3C` on `#FDECEC` (text `#A12F2F`) | Errors, destructive, Suspended |
| `neutral` | `#66788A` on `#EEF2F6` | Role tags, None level |
| Auth-only accent | `#2BB5B0`, `#8FDCD8`, lavender `#7C74D6` / `#EFEEFB` | [C] Logo mark and "Invoice paid" card on sign-in only. [O] Add lavender to the palette, or replace it with teal |
| Font | Inter 400/500/600/700 | Fallback stack as spec |
| Type scale | h1 28/700 · h2 20/700 · h3 16/600 · body 14 · label 13/600 · helper 12 · metric 26/700 · button 14/600 · auth h2 32/700 · auth headline 34–46/700 | |
| Radius | input/button 6 (auth 8) · card 10 · tag 999 · drawer 0 docked / 12 overlay | |
| Spacing | 4 · 8 · 12 · 16 · 20 · 24 · 32 | 20 is used for card and drawer padding. [O] Keep it or snap to 16/24 |
| Control height | 40 (app) · 48 (auth) · 32 (table actions) | Mobile targets ≥ 44 |
| Shadow | overlay `0 8px 24px rgba(16,42,67,0.14)` · drawer `-8px 0 24px rgba(16,42,67,0.06)` | Overlays only |

---

## 6. Decisions not defined by the mockups

**Corrections applied (confirmed with spec)**
1. Nav labels follow the spec (Overview, Work orders, Invoices, Administration) instead of the mockup (Dashboard, Jobs, Billing, Settings).
2. Products & services moved from the Administration sub-nav to the main nav.
3. Help added to the top bar. The "Updated just now" indicator was removed.
4. Tabs removed from the branch drawer and from Users & permissions, because they duplicated content.
5. Role tags use neutral colors. Permission "None" is neutral. A legend was added.
6. "Send invitation email" toggle removed.
7. One logo across all screens; the mockups used two different marks.
8. Decorative phone mockup: removed as a UI element on sign-in (it now exists only inside the background photo).

**Suggested (designer defaults)**
- Drawer docks from 1440px up and overlays below. Sub-nav hides below 1100px.
- Save and Discard are enabled only when the form is dirty. Route-leave guard.
- Main branch cannot be deactivated. Last Owner cannot be suspended or downgraded (item disabled + tooltip).
- Owner and Operations Manager are forced to All branches.
- "Remember me" is unchecked by default.

**Open product decisions**
- Company-level business hours page vs. branch-only hours.
- Destinations and scope of "Manage tax rates" and "Edit sequences".
- Behavior when "Use company billing settings" is off.
- Whether service ZIP codes drive anything in the MVP.
- Multiple Owners. Changing the main branch.
- Customer portal users: listed here or not.
- Locked / unverified account flows on sign-in. Supported languages and countries.
- Final permission matrix values.
- Lavender accent on sign-in.

---

## 7. Assets

| Asset | Status | Notes |
|---|---|---|
| `assets/signin-background.png` (1122×1402) | [C] Provided | Bottom-center `object-fit: cover`. [S] Export WebP/AVIF at 1x and 2x (≤ 300 KB), plus a solid `#102A43` fallback. [O] The image contains a baked-in phone UI and logo, which must be updated if the UI or brand changes |
| FieldOps logo (mark + wordmark) | [O] Needed as SVG | Light-on-navy and dark-on-white variants, plus a mark-only version for the collapsed sidebar and favicon. The prototype uses a vector approximation |
| Favicon / app icons | [O] Needed | 32, 180, 192, 512 |
| PrimeIcons 7 | [C] | `pi-th-large, pi-inbox, pi-file-edit, pi-wrench, pi-calendar, pi-users, pi-id-card, pi-box, pi-receipt, pi-chart-bar, pi-cog, pi-building, pi-map-marker, pi-clock, pi-user-edit, pi-dollar, pi-hashtag, pi-bell, pi-question-circle, pi-search, pi-upload, pi-plus, pi-ellipsis-h/v, pi-check-circle, pi-ban, pi-envelope, pi-shield, pi-link, pi-send, pi-eye(-slash), pi-globe, pi-exclamation-circle/triangle, pi-info-circle, pi-refresh, pi-history` |
| Inter font | [C] | Self-host (woff2) 400/500/600/700 |
| User avatars | [S] | Initials fallback via `p-avatar`. Photo upload is out of scope |

---

## 8. PrimeNG mapping (summary)

| UI | Component |
|---|---|
| Sidebar (mobile) / branch & invite drawers | `p-drawer` (position right; `modal` below 1440px) |
| Breadcrumb | `p-breadcrumb` |
| Buttons, text buttons, icon buttons | `p-button` (severity secondary / danger, `text`, `outlined`, `[rounded]=false`) |
| Text / email / tel | `p-inputtext` (+ `p-iconfield` / `p-inputicon` for search) |
| Password | `p-password` |
| Tax rate, next number | `p-inputnumber` |
| Selects (country, tz, state, currency, role, filters, expiry, language) | `p-select` |
| ZIP codes | `p-autocomplete [multiple]` (S) or `p-inputtext` |
| Time ranges | `p-datepicker [timeOnly]` |
| Toggles | `p-toggleswitch` |
| Checkboxes | `p-checkbox` |
| Logo upload | `p-fileupload mode="basic"` |
| Tables + paginator | `p-table` (lazy, sortable fields only) + built-in paginator |
| Row overflow | `p-menu [popup]` |
| Status / role / access tags | `p-tag` |
| Avatars | `p-avatar` (label, circle) |
| Inline notes, errors, read-only banner | `p-message` |
| Toasts | `p-toast` (top-right) |
| Confirmations | `p-confirmdialog` |
| Skeletons | `p-skeleton` |
| Tooltips (disabled items, icon buttons) | `p-tooltip` |
| Metric cards, section cards | Semantic HTML (`section`), not `p-card` |
