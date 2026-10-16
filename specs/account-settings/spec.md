# Account settings

| Field    | Value              |
| -------- | ------------------ |
| Feature  | `account-settings` |
| Type     | Full-stack         |
| Status   | APPROVED           |
| Created  | 2026-10-16         |
| Updated  | 2026-10-16         |
| Approved | 2026-10-16         |

## Context and objective

Signed-in users cannot change their own name, phone, photo or password; the
user menu offers only **Sign out**. This feature adds one Account settings area
with **My profile** (editable first name, last name, phone and profile photo;
read-only email, organization and role) and **Security** (change password while
keeping the current session), reachable from the user menu of the main shell and
the technician shell. Success means every signed-in internal user can maintain
their own account, other sessions are revoked on a password change, and no
request can read or change another user's account.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Signed-in internal user, any role (`owner`, `viewer`, `dispatcher`, `technician`, `accounting`, `operations_manager`, unknown code) | Open `/account/*`; read their own profile; edit their own first name, last name and phone; upload, replace and remove their own photo; change their own password | Change their email, organization or role; read or change another user's profile, photo or password; supply a user or organization identifier |
| Unauthenticated visitor | — | Open `/account/*` (redirect to `/auth/sign-in`); call `/account/*` endpoints (`401`) |
| System | Resolve user, organization and membership only from the validated session; detect photo type from file content; re-issue the current session cookie after a password change; write audit rows | Trust client user/organization identifiers, client MIME types or file extensions |

## Scope

- Account settings layout with section navigation (My profile, Security).
- My profile: personal information form, read-only email and membership, photo
  upload/replace/remove.
- Security: change password with current password, new password, confirmation
  and a live requirements checklist.
- User menu of the main shell redesigned per the user-menu design with
  **My profile**, **Account settings** and the existing **Sign out**.
- Technician shell **More** panel gains **Account settings**.
- Profile photo shown in the profile header, the top bar avatar and the user
  menu header.
- Schema amendment SA-22 (`user_photos`).
- Audit events for profile, photo and password changes.

## Non-goals

- Preferences section, language selection and translations, dark mode and theme
  persistence, Help & support (hidden until their own specs are implemented).
- Two-factor authentication, session list or per-session sign-out.
- Changing email, organization, role or branch access.
- Showing the photo anywhere else (Team, assignments, schedule, documents,
  portal).
- Changing the password policy of registration, invitation or recovery.
- Customer portal accounts.
- Changing the technician shell **My profile** link (`/team`).

## User flow

1. A signed-in user opens the user menu and selects **My profile** or
   **Account settings** (technicians: **More → Account settings**).
2. The system shows `/account/profile` inside the main shell with the profile
   loaded from `GET /account/profile`.
3. The user edits personal information and selects **Save changes**; the system
   saves it, shows a toast and refreshes the session so the top bar updates.
4. The user selects **Change photo**, picks a JPG or PNG; it uploads at once and
   every avatar updates. **Remove photo** returns to initials.
5. The user selects **Security**, enters the current and new password and the
   confirmation, and selects **Update password**.
6. The system verifies the current password, stores the new one, signs out the
   user's other sessions, keeps this one and shows a toast.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must provide `/account/profile` and `/account/security` inside the authenticated main shell for every signed-in role; `/account` redirects to `/account/profile`. The routes revalidate the session through the existing `authGuard`. |
| FR-02 | The account layout must show the BR-09 breadcrumb, heading and subtitle and a section navigation with exactly **My profile** and **Security**, marking the current section with `aria-current="page"`. No Preferences item exists. |
| FR-03 | The main shell user menu must show a header (avatar per BR-08, full name, role name), then **My profile** (→ `/account/profile`), **Account settings** (→ `/account`), a separator and **Sign out**. Sign out keeps every outcome of `authenticated-app-shell` FR-05. No Language, Dark mode or Help & support item exists. |
| FR-04 | The technician shell **More** panel must contain, in order, **My profile** (unchanged → `/team`), **Account settings** (→ `/account`) and **Sign out** (unchanged behavior). |
| FR-05 | `GET /account/profile` must return the session user's profile and the session membership's organization and role names; `PUT /account/profile` must validate per BR-01 and update only `first_name`, `last_name` and `phone` of the session user, ignoring any other body property. |
| FR-06 | My profile must show the profile header (avatar, full name, role name, **Change photo**, **Remove photo** when a photo exists, helper "JPG or PNG. Max 5 MB."), the Personal information form (First name, Last name, Email address read-only with "Your email address cannot be changed.", Phone number (optional)) and the Membership section (read-only Organization and Role with "Your organization and role are managed by an administrator."), plus **Save changes** and **Cancel**. |
| FR-07 | Saving the profile must show progress, ignore repeat submits, show the BR-10 success toast, mark the form pristine and reload the session so the top bar and user menu show the new name without a page reload. **Cancel** restores the last saved values and clears field errors. |
| FR-08 | The backend must provide retrieval, upload/replace and removal of the session user's photo per BR-03 and BR-04; the frontend uploads immediately on file choice with progress, validates per BR-03 first, updates every avatar (BR-08) and shows the BR-10 toasts. Photo actions never dirty the profile form. |
| FR-09 | `GET /sessions/current` and the `POST /sessions` success body must include `user.photoUpdatedAt` (ISO timestamp or `null`). |
| FR-10 | Security must show "Change password" with Current password, New password and Confirm new password (each with a show/hide toggle), the BR-02 requirements panel with live met/not-met state, **Update password**, **Cancel** (clears the three fields and errors) and the BR-09 info note. |
| FR-11 | `POST /account/password` must apply BR-05 and BR-06: verify the current password, store the new one, set `password_changed_at`, then re-issue the current session cookie so this session continues while every earlier session becomes invalid. On success the frontend clears the fields and shows the BR-10 toast. |
| FR-12 | Leaving My profile with unsaved form changes or Security with any non-empty field must show the existing discard-changes dialog; the dialog is bypassed once the session has been cleared (sign-out). |
| FR-13 | Profile, photo and password changes must write the BR-07 audit rows in the session organization. |
| FR-14 | The account pages must meet the responsive and accessibility rules in UI behavior. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Profile fields (strings trimmed; first failing rule wins; `400` keys as listed; client mirrors before sending): see the Profile field validation table. Unknown body properties (including `email`, `userId`, `organizationId`, `role`) are ignored. Empty phone is stored as `null`. | Both |
| BR-02 | Password policy (existing policy, onboarding BR-17 / recovery BR-09, unchanged; passwords never trimmed): `newPassword` required, 12–128 UTF-16 code units, not equal to the user's email ignoring case, not equal to `currentPassword`. `currentPassword` required, ≤128. `confirmPassword` is client-only: required and equal to `newPassword`. Requirements panel items: "12–128 characters", "Must not match your email", "Different from your current password", each with an icon plus text state (met / not met), never color alone. | Both |
| BR-03 | Photo file: `multipart/form-data` field `file`; ≤5 MB (5,242,880 bytes) else `errors.file` "Choose a file of 5 MB or smaller."; type detected from content only (PNG or JPEG signature); anything else, or a detected type not matching the declared part type, → `errors.file` "Choose a JPG or PNG file.". Request body >6 MB → `413`; non-multipart → `415`. Client mirrors size and type checks before uploading. | Both |
| BR-04 | Photo responses carry the stored MIME type, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Content-Disposition: inline` and `Cache-Control: no-store`. The frontend displays the photo only through an `<img>` element whose URL changes with `photoUpdatedAt`. Photo operations change `user_photos` only, not `users.updated_at`. | Both |
| BR-05 | Password change check order, first failing check wins: 1. per-user failure limit reached (`429`, BR-06); 2. content type, size, body, required/length (`415`, `413`, `400`); 3. current password verification (`400` `currentPassword` "Your current password is incorrect.", counts one failure); 4. `newPassword` ≠ email (`400` `newPassword` "Choose a password that is different from your email."), ≠ current (`400` `newPassword` "Choose a password that is different from your current password."). Success, in one transaction: `users.password_hash` (onboarding BR-20 PBKDF2 format), `password_changed_at = now`, `updated_at = now`, audit row; after commit the current session cookie is re-issued with `fieldops:signed_in_at` not earlier than the new `password_changed_at`, keeping the session's organization, membership and remember-me persistence. Recovery BR-15 then rejects every earlier session (`401`). `204`. If the cookie re-issue fails after the commit, the new password stays in effect, the response is the generic `500`, and the current session is rejected at its next request (`401`, existing navigation to Sign In), where the new password signs in. | Backend |
| BR-06 | Failure limit (in memory, like sign-in BR-11): 5 failed current-password verifications per user id per 15-minute fixed window. While 5 failures are counted, every `POST /account/password` of that user returns `429` with `Retry-After` (seconds until the window ends) without verifying anything. A success does not reset the count. Sign-in and recovery limits are unchanged. | Backend |
| BR-07 | Audit (existing audit-trail rules, session organization, actor = session user, entity `user` = session user id): `user.profile_updated` only when at least one value changes, metadata = changed field names only (`firstName`, `lastName`, `phone`); `user.photo_updated`; `user.photo_removed` only when a photo existed; `auth.password_changed`. Each row records the client IP address in `audit_logs.ip_address` as sign-in and sign-out do (IPv4-mapped addresses stored as IPv4; null when unknown). Catalog labels: "Profile updated", "Profile photo updated", "Profile photo removed", "Password changed". Never names, phone, passwords, hashes or photo bytes. | Backend |
| BR-08 | Avatar: the photo when `photoUpdatedAt` is not null and the image loads; otherwise the initials of `authenticated-app-shell` BR-02. Avatars are decorative (`alt=""`); the accessible names stay as in the shell. | Frontend |
| BR-09 | Copy. Breadcrumb "Account / My profile" or "Account / Security"; `h1` "Account settings"; subtitle "Manage your profile and security settings."; section nav landmark "Account settings sections". Security header "Security" / "Update the password used to access your account."; info note "Changing your password keeps you signed in here and signs you out on other devices." Buttons **Save changes**/"Saving…", **Update password**/"Updating…", **Change photo**, **Remove photo**, **Cancel**. Password toggles' accessible names "Show current password" / "Hide current password" (same pattern for new and confirmation). | Frontend |
| BR-10 | Feedback. Toasts: "Profile updated", "Photo updated", "Photo removed", "Password updated". Profile load error: "We couldn't load your profile. Try again." with **Retry**. Profile save failure (`400` without known keys, `413`, `415`, `5xx`, network): "We couldn't save your changes. Try again." Photo failure: field message for `400 file`, otherwise "We couldn't update your photo. Try again." under the photo control. Password failure (`400` without known keys, `5xx`, network): "We couldn't update your password. Try again."; `429`: "Too many attempts. Try again in {n} minutes." (`n` = `Retry-After`/60 rounded up, "1 minute" singular) with **Update password** disabled for that period. Any `400` field message from the server is shown under its field. Inputs are kept on every failure. | Frontend |
| BR-11 | Every `/account/*` response carries `Cache-Control: no-store`. Errors never contain passwords, hashes or exception text; `500` is the generic ProblemDetails. Logs never contain names, phone, email, passwords, hashes, request bodies or photo bytes. | Backend |

### Profile field validation

| Field (`400` key) | Required | Max | Format | Messages |
| ----------------- | -------- | --- | ------ | -------- |
| `firstName` | Yes | 100 | — | "This field is required." / "Use 100 characters or fewer." |
| `lastName` | Yes | 100 | — | "This field is required." / "Use 100 characters or fewer." |
| `phone` | No | 40 | Existing onboarding phone format | "Use 40 characters or fewer." / "Enter a valid phone number." |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| No photo | Photo stored | `PUT /account/photo` | Session user | BR-03 |
| Photo stored | Photo replaced / removed | `PUT` / `DELETE /account/photo` | Session user | BR-03 |
| Earlier sessions valid | Earlier sessions invalid, current session re-issued | `POST /account/password` `204` | Session user | BR-05, BR-06 |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  `users` (`first_name`, `last_name`, `email` read-only, `phone`,
  `password_hash`, `password_changed_at`, `updated_at`); `organization_users`
  and `roles` read-only for the membership; `organizations.name` read-only;
  existing audit table through the audit-trail writer.
- Schema amendment SA-22 (approved by the user 2026-10-16, OD-04): new table
  `CREATE TABLE user_photos (user_id uuid PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE, content_type varchar(40) NOT NULL CHECK (content_type IN ('image/png','image/jpeg')), content bytea NOT NULL, size_bytes integer NOT NULL CHECK (size_bytes BETWEEN 1 AND 5242880), created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now())`.
  Pattern of `organization_logos`. The photo belongs to the user (not to an
  organization), so it carries no `organization_id`. No other table changes.
  The implementation adds SA-22 to the schema document; migration generation
  needs `--generate-migration` and is never applied by agents.
- `users` is shared by every organization the user belongs to: name, phone,
  photo and password changes apply in all of them.

## Tenant isolation and authorization

- Organization context: user, organization and membership come only from the
  validated session cookie (sign-in FR-07). Every endpoint acts on the session
  user only; there is no user identifier in any path, query or body.
- Client-provided organization or user identifiers: none accepted; such body
  properties are ignored (BR-01) and never change which user is read or written.
- Another user's profile, photo or password: unreachable by construction; a
  request carrying another user's or organization's identifier changes only the
  session user and writes audit only in the session organization.
- Permission: any valid internal session (`[Authorize]`), no role policy. An
  invalid, revoked or suspended-membership session gets the existing `401`.

## API contracts

Contract status: Final.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/account/profile` | — | `200` `{ firstName, lastName, email, phone \| null, organization: { name }, role: { name }, photo: { contentType, sizeBytes, updatedAt } \| null }` | `401` | Signed-in |
| PUT | `/account/profile` | JSON `{ firstName, lastName, phone }` | `200` same body as GET | `400` keys `firstName`, `lastName`, `phone` (keyless for malformed body) · `401` · `413` · `415` | Signed-in |
| GET | `/account/photo` | — | `200` image bytes, BR-04 headers | `401` · `404` none | Signed-in |
| PUT | `/account/photo` | `multipart/form-data`, field `file` | `200` `{ contentType, sizeBytes, updatedAt }` | `400` `errors.file` · `401` · `413` · `415` | Signed-in |
| DELETE | `/account/photo` | — | `204` (also when none) | `401` | Signed-in |
| POST | `/account/password` | JSON `{ currentPassword, newPassword }` | `204` + re-issued session cookie | `400` keys `currentPassword`, `newPassword` (keyless for malformed body) · `401` · `413` · `415` · `429` + `Retry-After` | Signed-in |
| GET | `/sessions/current` (changed) | — | `200` existing body + `user.photoUpdatedAt` | Unchanged | Signed-in |
| POST | `/sessions` (changed) | Unchanged | `200` existing body + `user.photoUpdatedAt` | Unchanged | Anonymous |

## UI behavior and states

Design references (approved):

- `design/assets/Ajustes de cuenta_ perfil y seguridad.png` — My profile.
- `design/assets/Cambio de contraseña en FieldOps.png` — Security.
- `design/assets/Interfaz de configuración de cuenta unificada.png` — layout
  reference only (heading, section navigation, card); its Preferences content is
  out of scope.
- `design/assets/user menu.png` — main user menu only.

Adaptations: Preferences item, Language, Dark mode and Help & support are not
rendered; subtitle per BR-09; requirements panel lists the BR-02 policy instead
of the mockup's; info note per BR-09; **Remove photo** added beside
**Change photo** when a photo exists. The page behind the menu mockup is not a
reference.

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| My profile | Skeleton for header and form; no values shown | Not applicable: profile always exists; no phone shows the placeholder; no photo shows initials | Load: BR-10 message + **Retry**. Save/photo: BR-10 | Not applicable: every signed-in role | Toast; form pristine; avatars updated |
| Security | Not applicable: no data loaded | Not applicable | BR-10 field, generic and `429` messages | Not applicable | Toast; fields cleared; session kept |
| User menu / More panel | Not applicable | Not applicable | Sign-out error unchanged | Same items for every role | Navigation to `/account/*` |

- Mockups: the four references above (Approved).
- Responsive (existing `md` 48rem / `lg` 64rem breakpoints): from `lg` the
  section navigation sits beside the content card; below `lg` it is a full-width
  list above the card; below `md` the Personal information and Membership fields
  stack in one column, the profile header stacks the photo actions under the
  name, and the password requirements panel moves below the fields. No
  horizontal page scroll at 320px; touch targets ≥44px below `lg`.
- Accessibility: one `h1`; section navigation in a labelled `nav`; every input
  has a visible label; read-only fields are `readonly` (focusable, announced),
  not disabled; field errors are linked to their inputs; the requirements panel
  updates politely (`aria-live="polite"`); password toggles are buttons with the
  BR-09 names and pressed state; menu keyboard behavior as in
  `authenticated-app-shell`; visible focus everywhere.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| No valid session | `401`; frontend navigates to `/auth/sign-in` | None |
| Profile field invalid | `400` field keys; message under the field | None |
| Profile load fails | BR-10 load error + **Retry** | None |
| Profile save fails (`5xx`, network, other) | BR-10 save message; inputs kept | None |
| Photo too large / wrong type / spoofed type | `400` `errors.file`; message under the photo control | None |
| Photo body >6 MB / non-multipart | `413` / `415`; BR-10 photo message | None |
| Current password wrong | `400` `currentPassword` "Your current password is incorrect." | Failure counted |
| New password equals email or current | `400` `newPassword` BR-05 message | None |
| Failure limit reached | `429` + `Retry-After`; BR-10 message, submit disabled | None |
| Password change fails (`5xx`, network, other) before the commit | BR-10 password message; inputs kept | None (transaction rolled back) |
| Cookie re-issue fails after the commit | Generic `500`; BR-10 password message; next request `401` → Sign In | New password stored; every session, including this one, invalid |
| Earlier session after a password change | `401`, cookie cleared (recovery BR-15) | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A signed-in user in the main shell (parameterized: `owner`, `dispatcher`, `technician`) | They open the user menu | It shows the avatar, full name and role name, then **My profile**, **Account settings**, a separator and **Sign out**, and no Language, Dark mode or Help & support item; **My profile** goes to `/account/profile`; **Account settings** goes to `/account`, which ends on `/account/profile` |
| AC-02 | A signed-in `technician` in the technician shell | They open **More** and select **Account settings** | The panel lists **My profile** (`/team`), **Account settings** and **Sign out** in that order; the browser ends on `/account/profile` and the technician can open **Security** |
| AC-03 | A signed-in user | They select **Sign out** in the redesigned menu and receive `204`/`401`, or a failure (parameterized) | The outcomes of `authenticated-app-shell` AC-11 to AC-13 hold unchanged |
| AC-04 | A signed-in user on `/account/profile` | The page renders, then they select **Security** | Breadcrumb, `h1` and subtitle match BR-09; the section navigation shows only **My profile** and **Security**, with `aria-current="page"` on the current one; after selection the browser is on `/account/security` and the breadcrumb reads "Account / Security" |
| AC-05 | No valid session | The visitor opens `/account`, `/account/profile` or `/account/security`, or calls any `/account/*` endpoint (parameterized) | The browser ends on `/auth/sign-in` without rendering the shell; every endpoint returns `401` |
| AC-06 | A user with phone and no photo | My profile loads | A skeleton shows while loading; then First name, Last name and Phone are editable with the stored values, Email, Organization and Role are read-only with their helper texts, the header shows initials, full name, role, **Change photo** and no **Remove photo** |
| AC-07 | `GET /account/profile` fails | My profile loads, then the user selects **Retry** and it succeeds | "We couldn't load your profile. Try again." is shown with **Retry**; after retry the form shows the profile |
| AC-08 | A user editing First name " Ana ", Last name "Lopez " and an empty phone | They select **Save changes** | `PUT /account/profile` stores "Ana", "Lopez" and `null`; "Profile updated" is shown; the form is pristine; the top bar and user menu show "Ana Lopez" without a page reload; while saving the button shows "Saving…" and a second click sends no request |
| AC-09 | Invalid profile values (parameterized from the Profile field validation table) | The user saves, or the API receives them directly | The client shows the table message under the field and sends no request; the API returns `400` with the table key and message and changes nothing |
| AC-10 | Users A (organization 1) and B (organization 2) | A sends `PUT /account/profile` with valid names plus `email`, `userId` = B, `organizationId` = organization 2 and `role` | Only A's names/phone change; A's email, membership and role and all of B's data are unchanged; one `user.profile_updated` row exists in organization 1 with only changed field names; no row exists in organization 2; saving identical values writes no row |
| AC-11 | A user with unsaved profile edits and field errors, or non-empty Security fields with errors (parameterized) | They select **Cancel** | My profile shows the last saved values; Security shows the three fields empty; no field error remains; no request is sent and the user stays on the page |
| AC-12 | A valid profile edit | Save fails with `500` or a network error (parameterized) | "We couldn't save your changes. Try again." is shown, the edited values are kept and nothing is stored |
| AC-13 | A user without a photo | They choose a valid PNG or JPEG ≤5 MB (parameterized) | The upload starts at once with progress; "Photo updated" is shown; the profile header, the top bar avatar and the user menu header show the photo; **Remove photo** appears; `photoUpdatedAt` is not null in `GET /sessions/current`; one `user.photo_updated` row exists; the profile form stays pristine |
| AC-14 | A file larger than 5 MB, a GIF, or a non-image renamed `.png` (parameterized) | The user chooses it, or it is sent directly to `PUT /account/photo` | The client shows the BR-03 message without uploading; the API returns `400` `errors.file` with the BR-03 message (`413` above 6 MB, `415` non-multipart) and the stored photo is unchanged |
| AC-15 | A user with a photo | They select **Remove photo** | `DELETE /account/photo` returns `204`; "Photo removed" is shown; every avatar shows initials; **Remove photo** disappears; one `user.photo_removed` row exists; a second `DELETE` returns `204` and writes no row |
| AC-16 | A user with a stored photo, and one without | They request `GET /account/photo` | With a photo: `200`, the stored MIME type and every BR-04 header; without: `404`; the endpoint accepts no identifier, so no other user's photo is reachable |
| AC-17 | Security open | The user types a new password and edits it (parameterized: 11 chars, 12 chars, equal to email ignoring case, equal to the current password field) | Each requirement shows met or not met with icon and text, updating as they type |
| AC-18 | Security with an empty field, an unmet requirement or a confirmation that differs (parameterized) | The user selects **Update password** | The field messages ("This field is required.", the requirement message or "Passwords don't match.") are shown and no request is sent |
| AC-19 | User A signed in on client 1 (remember-me) and earlier on client 2 | A changes the password from client 1 with a valid current password | `204`; "Password updated" is shown and the fields are cleared; client 1 keeps working (`GET /sessions/current` `200`) with a persistent cookie; client 2 gets `401`; `password_changed_at` is set; the new password signs in and the old one returns `401`; one `auth.password_changed` row exists; no password or hash appears in responses or logs |
| AC-20 | A wrong current password | The user submits | `400` `currentPassword` "Your current password is incorrect." under Current password; the stored password is unchanged; inputs are kept |
| AC-21 | User A with 5 failed verifications in the current window | A submits again, even with the correct current password | `429` with `Retry-After`; the password is unchanged; the page shows "Too many attempts. Try again in {n} minutes." and **Update password** is disabled for that period; another user is not limited |
| AC-22 | A valid current password and a new password equal to the email (any case) or to the current password (parameterized), sent directly to the API | `POST /account/password` | `400` `newPassword` with the BR-05 message; nothing changes |
| AC-23 | A valid password change | It fails with `500` or a network error (parameterized) | "We couldn't update your password. Try again." is shown; inputs are kept; while in flight the button shows "Updating…" and repeat submits send no request; on a `500` raised before the commit the stored password and sessions are unchanged; when the commit succeeds but the cookie re-issue fails (`500`), the new password is stored and the next request of this session returns `401` and leads to Sign In, where only the new password works |
| AC-24 | Viewports 320, 768 and 1280 px, keyboard-only use (parameterized) | The user moves through My profile and Security | Layout matches the responsive rules with no horizontal page scroll; every control is reachable with visible focus; read-only fields are focusable and announced; password toggles expose the BR-09 names and pressed state; errors are announced with their fields |
| AC-25 | A user with unsaved profile edits, or non-empty Security fields (parameterized) | They navigate to another route; separately, they sign out and receive `204` | Navigating shows the existing discard-changes dialog (**Keep editing** keeps the page and input, **Discard** navigates); signing out goes to `/auth/sign-in` with no dialog |

## Testing requirements

Parameterized cases count as one method.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Password change rule order (BR-05) and the per-user failure window (BR-06), only if they live outside the endpoint pipeline. ≤2 methods | AC-20, AC-21, AC-22 |
| Backend integration | Profile get/update with trimming, validation table, ignored identifiers and audit; photo upload/replace/remove/get with content sniffing, size, `413`/`415`, headers and audit; password change success keeping the current session and revoking an earlier one, audit rows with client IP, wrong current, policy `400`s, `429` after 5 failures; `photoUpdatedAt` in the session body; `401` without a session. ≤7 methods | AC-05, AC-08 to AC-10, AC-13 to AC-16, AC-19 to AC-22 |
| Authorization/tenant isolation | One representative test: identifiers of another user/organization in the body are ignored, audit only in the session organization; plus the earlier-session revocation in AC-19 | AC-10, AC-19 |
| Frontend component/service | Routes and redirect plus both shells' menu items; profile load/error/retry, save outcomes, cancel and session refresh; photo client validation and avatar update; security checklist, client validation and response outcomes including `429` timer; discard dialog and sign-out bypass. ≤6 methods | AC-01 to AC-04, AC-06 to AC-09, AC-11 to AC-15, AC-17, AC-18, AC-20, AC-21, AC-23, AC-25 |
| User visual QA | Fidelity to the four design references, menu, responsive layouts, keyboard and focus, using a throwaway account on the local database | AC-01, AC-02, AC-04, AC-06, AC-24 |

## Dependencies

- `specs/authenticated-app-shell/spec.md` (AUDITED): user menu, Sign out
  outcomes, BR-02 initials, cleared-session bypass. FR-03 here supersedes its
  one-item menu; Sign out behavior is unchanged.
- `specs/technician-todays-jobs/spec.md` (technician shell **More** panel):
  FR-04 adds one item; existing items unchanged.
- `specs/password-recovery/spec.md`: password policy (BR-09),
  `password_changed_at` and session revocation (BR-15) reused unchanged.
- `specs/sign-in/spec.md`: session cookie, revalidation, rate-limit pattern.
- `specs/design-17-company-setup-completion/spec.md`: logo upload pattern
  (BR-07/BR-08) for the photo.
- `specs/audit-trail/spec.md`: audit writer and catalog; four new event codes.
- Existing shared discard-changes dialog.
- Implementation classification for `spec-impl`: Backend TARGETED (new
  controller, SA-22 entity/mapping, cookie re-issue); Frontend TARGETED (new
  feature route, both shells' menus, session model field).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Technicians open `/account/*` inside the main shell, as they already do for `/team`. |
| AS-02 | The account feature lives under `frontend/src/app/features/account/`; avatars reuse one small shell-level helper, moved to `shared/` only if a second feature needs it. |
| AS-03 | The cookie re-issue reuses the existing session cookie issuer with the current membership; no session table is introduced. |
| AS-04 | Photo content detection reuses the organization-logo PNG/JPEG signature checks. |
| AS-05 | The profile reload after save uses the existing session reload already used by Company settings. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Password policy vs mockup (8+, upper/lower/number) | Existing policy / mockup policy everywhere / stricter here | Yes | Existing 12–128 and ≠ email, plus ≠ current; panel adapted (2026-10-16) |
| OD-02 | Sessions after password change (recovery BR-15 would revoke the current one) | Re-issue current cookie / keep other sessions | Yes | Re-issue the current cookie after updating `password_changed_at`; all earlier sessions invalid; note copy per BR-09 (2026-10-16) |
| OD-03 | Wrong current password and throttling | Proposed | Yes | `400` `currentPassword`; 5 failures per user per 15 min → `429` + `Retry-After` (2026-10-16) |
| OD-04 | Photo storage and removal | SA-22 `user_photos` / none | Yes | SA-22 following `organization_logos`; **Remove photo** included (2026-10-16) |
| OD-05 | Where the photo appears | Profile, top bar, menu / more | Yes | Those three; `user.photoUpdatedAt` in the session body (2026-10-16) |
| OD-06 | Audit | Audit / none | Yes | Four events, no values (2026-10-16) |
| OD-07 | Routes and menu targets | Proposed | Yes | `/account` → `/account/profile`; My profile → `/account/profile`; Account settings → `/account` (2026-10-16) |
| OD-08 | Preferences, Language, Dark mode, Help & support | Hide / disabled | Yes | Hidden until their specs are implemented (2026-10-16) |
| OD-09 | Roles and technician access | Proposed | Yes | All internal roles; technician **More** adds Account settings → `/account`, My profile → `/team` kept (2026-10-16) |
| OD-10 | Unsaved changes and session refresh | Proposed | Yes | Cancel resets; discard dialog on leave; session reload after save (2026-10-16) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-05 |
| FR-02 | AC-04 |
| FR-03 | AC-01, AC-03 |
| FR-04 | AC-02 |
| FR-05 | AC-08, AC-09, AC-10 |
| FR-06 | AC-06 |
| FR-07 | AC-07, AC-08, AC-11, AC-12 |
| FR-08 | AC-13, AC-14, AC-15, AC-16 |
| FR-09 | AC-13 |
| FR-10 | AC-17, AC-18 |
| FR-11 | AC-19, AC-20, AC-21, AC-22, AC-23 |
| FR-12 | AC-25 |
| FR-13 | AC-10, AC-13, AC-15, AC-19 |
| FR-14 | AC-24 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-16 | — → DRAFT     | Created |
| 2026-10-16 | DRAFT → DRAFT | Fixed validation warnings: W1 exact too-long messages in the profile field table; W2 cookie re-issue failure after commit defined (BR-05, Error behavior, AC-23); W3 AC-11 limited to Cancel and new AC-25 for the discard dialog and sign-out bypass (FR-12 traceability moved to AC-25); W4 audit rows record the client IP (BR-07) |
| 2026-10-16 | DRAFT → APPROVED | Approved by user via /spec approve |
