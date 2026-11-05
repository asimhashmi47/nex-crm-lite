# Acceptance criteria

Every criterion maps to at least one test. Test names carry the criterion id in a `[Trait("AC", "...")]` (.NET) or in the test title (client and E2E). The matrix at the end lists them.

## Auth (AUTH)

| ID | Criterion |
|---|---|
| AUTH-1 | Valid username and password return 200 with an access token, its expiry and the user (id, name, role, canDelete). The username is matched case-insensitively and trimmed; the password is not trimmed. |
| AUTH-2 | A wrong password and an unknown username both return 401 with the same message. |
| AUTH-3 | A deactivated user cannot sign in (401) even with the right password, and every existing token stops working on every endpoint. |
| AUTH-4 | Empty or over-long username (> 50) or password (> 100) return 400 with field errors. |
| AUTH-5 | Every endpoint except sign-in, auth config and health returns 401 without a token, with an expired token, with a wrong signature, or with `alg: none`. |
| AUTH-6 | `/api/auth/config` lists the demo accounts only when demo mode is on. |
| AUTH-7 | Sign-in is rate limited per client address. Five failures for one account from one address, or twenty from anywhere, lock it for 15 minutes, even for the right password (429 with the minutes left). `X-Forwarded-For` is only honoured from configured proxies. |
| AUTH-8 | A successful sign-in records `LastLoginAt` from the injected clock. |
| AUTH-9 | Signing out ends the session on the server: every token issued to that user so far returns 401. |

## Permissions (PERM)

| ID | Criterion |
|---|---|
| PERM-1 | Sales reps cannot delete any record (403). Admins and managers can (204). |
| PERM-2 | Every saved record needs an active owner; an unknown or inactive owner returns 400. |
| PERM-3 | All signed-in users can read and edit every record of the team (single-team CRM, no per-owner scoping). |
| PERM-4 | Only an admin or manager may move a colleague's record to another owner (403 otherwise); a rep may hand over their own. |

## Companies (CO)

| ID | Criterion |
|---|---|
| CO-1 | Creating a valid company returns 201 with the saved record; text is trimmed and blank optional fields are stored as null. |
| CO-2 | Company names are unique, ignoring case and surrounding spaces (409), also under concurrent creates (enforced by a unique index). |
| CO-3 | Validation: name 1–120, industry required, phone digits/spaces/`+-()`, employees 0–1,000,000, revenue ≥ 0, owner required (400 with field errors). |
| CO-4 | Updating or deleting an unknown id returns 404. |
| CO-5 | A company with open deals cannot be deleted (409); otherwise it is archived and disappears from grids, lookups and the dashboard. |
| CO-6 | The grid shows contact count, open-deal count and won revenue, ignoring archived rows. |

## Contacts (CT)

| ID | Criterion |
|---|---|
| CT-1 | Email is stored lower-case and is unique ignoring case (409), also under concurrent creates. |
| CT-2 | Invalid email or an unknown company returns 400. |
| CT-3 | Names accept Unicode (e.g. "Zoë", "محمد", "李"). |
| CT-4 | Updating an existing contact keeps its own email without a duplicate error. |

## Leads (LD)

| ID | Criterion |
|---|---|
| LD-1 | Score must be 0–100 and estimated value ≥ 0; the boundaries 0 and 100 are accepted. |
| LD-2 | Status "Converted" cannot be set through save (400); only Convert sets it. |
| LD-3 | Converting a qualified lead (and only a qualified one) opens a Prospecting deal for the estimated value, due in 45 days, and marks the lead Converted with the deal id. |
| LD-4 | Convert reuses a company with the same name (ignoring case) and a contact with the same email; otherwise it creates them. |
| LD-5 | Converting twice, or converting a lead that is not Qualified (New, Contacted, Unqualified), returns 400. A converted lead cannot be edited (409). |
| LD-6 | Converting the same lead many times at once creates exactly one deal. |

## Deals (DL)

| ID | Criterion |
|---|---|
| DL-1 | Amount must be > 0, title 1–150, and an expected close date is required within the last 5 / next 10 years (400 otherwise). |
| DL-2 | Each stage sets its default probability (10/25/50/75/100/0). |
| DL-3 | Moving to Won or Lost stamps `ClosedAt` from the injected clock; moving back to an open stage clears it. |
| DL-4 | Reps may override probability on open deals only; closed deals stay at 0 or 100; values are clamped to 0–100. |
| DL-5 | `PATCH /stage` with an unknown stage returns 400 and an unknown id returns 404. |
| DL-6 | The board has six columns. Counts and totals cover every deal in a column; cards are capped at 40, largest first. Won and Lost show only the last 90 days. The owner filter applies to everything. |
| DL-7 | An unknown company or contact returns 400. |

## Tasks (TK)

| ID | Criterion |
|---|---|
| TK-1 | Subject 1–150, notes ≤ 1000, and a due date is required within the last 5 / next 10 years (400 otherwise). |
| TK-2 | Marking done sets `CompletedAt` and the contact's `LastContactedAt`; reopening clears `CompletedAt`. |
| TK-3 | A task is overdue when it is not done and its due time is before now (injected clock). |

## Grids and export (GQ)

| ID | Criterion |
|---|---|
| GQ-1 | Page size is clamped to 1–500 and page to 1–last page. |
| GQ-2 | Sorting and filtering only accept fields the DTO exposes; unknown fields are ignored, never an error or SQL. |
| GQ-3 | Column filter syntax: text contains, numbers `>`, `>=`, `<`, `<=`, `a..b`, dates `yyyy-mm-dd` or ranges, enums comma lists, booleans. |
| GQ-4 | Advanced rules combine with AND or OR; sums cover all filtered rows, not just the page. |
| GQ-5 | Global search matches any text column, case-insensitively, including `%` and `_` as literal characters. |
| GQ-6 | Export writes the visible columns of all filtered rows to xlsx or csv; csv cells starting with `= + - @` are escaped; no valid columns returns 400. |

## Dashboard (DB)

| ID | Criterion |
|---|---|
| DB-1 | KPIs are correct for a known data set: open pipeline, weighted forecast, won this/last month, win rate and average deal (last 90 days), new leads, overdue and due-today tasks. |
| DB-2 | With no data, every KPI is 0 and nothing divides by zero. |
| DB-3 | The trend always has 12 months, oldest first, with zeros for empty months. |
| DB-4 | "On your plate" shows open tasks due from 14 days ago to 3 days ahead, soonest first, at most 8. |

## Lookups (LK)

| ID | Criterion |
|---|---|
| LK-1 | Lookups return at most 25 matches, filtered by term, and never archived records. |

## Cross-cutting (X)

| ID | Criterion |
|---|---|
| X-1 | Errors are ProblemDetails; 500s never include a stack trace outside Development. |
| X-2 | API responses carry `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and a CSP. |
| X-3 | Unknown `/api/*` routes return a 404 ProblemDetails. |
| X-4 | `/health` is anonymous and reports the database. |
| X-5 | `CreatedAt` and `UpdatedAt` come from the injected clock. |
| X-6 | Creates, updates (with owner changes), deletes, conversions, stage moves, exports and sign-ins are written to an audit log with user, time and address; only admins can read it. |
| X-7 | A POST with an `Idempotency-Key` returns the first result when repeated; the same key with a different body returns 422. |
| X-8 | Request bodies over 1 MB are refused with 413 before they are read. |
| X-9 | Each user is limited to 600 API calls and 10 exports a minute (429). |
| X-10 | Runtime counters (process, GC, thread pool, SQL statement timings) are available to admins only, and only when `Diagnostics:Enabled` is on (404 otherwise). |

## Client (UI)

| ID | Criterion |
|---|---|
| UI-1 | Toasts sit top right for 10 s with a progress bar, and render their text as plain text. |
| UI-2 | Signed-out users are sent to sign-in with a return URL; signed-in users who open sign-in go to the dashboard. |
| UI-3 | Journey: sign in → dashboard shows KPIs → open the pipeline. |
| UI-4 | Journey: create a contact; required fields show errors first. |
| UI-5 | Journey: convert a qualified lead and see the toast. |
| UI-6 | Journey: tick off a task. |
| UI-7 | Sales reps do not see delete buttons. |
| UI-8 | Signing out from the account menu kills the token on the server. |
| UI-9 | Only admins see and open the audit log. |

## Assumptions

- A1. One team per installation; there is no tenant model (NexCRM full has one, Lite deliberately does not).
- A2. All dates are the office's local time. The client sends local date-times without an offset and the server stores them as-is.
- A3. Money is a single currency (USD) stored as REAL in SQLite. Values up to 1e9 stay exact to the cent within double precision.
- A4. Demo accounts with password = username exist only when demo mode is on.

## Open questions (none block implementation)

- Q1. Should sales reps be limited to records they own? **Interim answer:** the minimum fix is in place (PERM-4: only managers re-own a colleague's record); stricter own-records-only scoping is still open.
- Q2. ~~Should logout revoke the token server-side?~~ Yes, done (AUTH-9).
- Q3. ~~Is an audit trail required?~~ Yes, done (X-6).
