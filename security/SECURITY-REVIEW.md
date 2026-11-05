# Security review and pentest: NexCRM Lite

**Scope:** every API endpoint and the Angular client, Development build on `localhost` (API 5090, client 4300), seeded demo database. Nothing was run against any other system.
**Mode:** report only. No application code was changed in this phase.
**Evidence:** [`security/pentest-probes.mjs`](pentest-probes.mjs) (25 probes; run with `node --experimental-sqlite security/pentest-probes.mjs`), the E2E XSS journey (`e2e/journeys.test.mjs`, UI-1), a browser open-redirect probe, and the integration tests in `tests/NexCrm.IntegrationTests`.
**Date:** 2026-11-05

## Status after remediation (Phase 8)

All 15 actionable findings are **fixed and retested**; the two Info items are accepted. The same probe script was run before and after, against the same seeded database.

| Probe | Before | After |
|---|---|---|
| AC-2 Rep re-owns a manager's deal | `PUT …ownerId=3` → **200**, owner changed | **403**, owner unchanged |
| AU-2 Token replayed after sign-out | **200** (no server sign-out) | `POST /api/auth/logout` 204, replay **401** |
| AU-3 Deactivated user's token | `/api/dashboard` **200** | **401** on every endpoint |
| BL-1 Convert a New lead | **200** | **400** "Qualify the lead before converting it." |
| BL-2 50 parallel creates, same company | 201×**8**, 409×42 | 201×**1**, 409×49 |
| BL-3 50 parallel creates, same email | **12** contacts | **1** contact |
| BL-5 Replayed create | two deals | 5 parallel POSTs with one `Idempotency-Key` → **one** deal; same key, other body → **422** |
| BL-6 Close date 9999-12-31 | **201** | **400** |
| RE-1 20 MB body | read and parsed, 400 after 126 ms | **413** with `Connection: close`, nothing read (31 ms) |
| RE-3 30 parallel exports | 30×200 | 200×8, **429×22** (10/min per user) |
| RE-4 700 calls from one user | all served | 600 served, **100 throttled**; another user still 200 |
| RE-5 Brute force | throttled at attempt 16 per address, no account lock | locked from attempt **6**; right password while locked **429**; spoofed `X-Forwarded-For` **429** |
| CF-1 Headers | `Server: Kestrel`; no HSTS | no `Server` header; HSTS + HTTPS redirect outside Development (integration test over HTTPS) |
| Probe summary | 25 probes, **14** needing attention | 25 probes, **0** needing attention |

| ID | Status | Fix | Proof (test) |
|---|---|---|---|
| F-01 | **Fixed** | `OnTokenValidated` checks on every request that the user is active and the token's session version is current | `DeactivatedUser_LosesAccess_WithTheTokenTheyAlreadyHave`, `DeactivatedUser_HasNoCurrentSession` |
| F-02 | **Fixed** | Tokens carry a session version (`sv`); `POST /api/auth/logout` bumps it, so every token issued so far dies. The client calls it on sign-out. Lifetime stays 8 h but is now revocable | `Logout_RevokesTheToken_OnTheServer`, `Logout_BumpsTheSessionVersion_SoOldTokensFail`, E2E UI-8 |
| F-03 | **Fixed** (minimum option of Q1) | Reps still work every team record, but only an admin or manager may move a colleague's record to another owner; reps may hand over their own | `SalesRep_CannotReownAColleaguesRecord_ButManagersCan`, `Rep_ReassigningAColleaguesRecord_IsForbidden_AndNothingIsSaved` |
| F-04 | **Fixed** | `Company.NameKey` (trimmed, lower-cased) with a unique index filtered on `IsDeleted = 0`; violations become 409. The migration archives existing duplicates first (oldest kept) | `ParallelCreates_OfOneCompanyName_MakeExactlyOneRow`, `DatabaseRejectsADuplicateNameKey_EvenWhenTheCheckIsBypassed`, `UniqueIndexViolation_BecomesAConflict_WithTheDefaultMessage` |
| F-05 | **Fixed** | Unique filtered index on `Contacts.Email` | `ParallelCreates_OfOneContactEmail_MakeExactlyOneRow` |
| F-06 | **Fixed** | `AuditLog` table written in the same transaction as the change: created, updated (with owner changes), archived, lead converted, deal stage changed, exported, signed in, sign-in failed, signed out. Admin-only `POST /api/audit/query` and an Audit log page. Every request log line names the user | `SensitiveActions_AreAudited_AndOnlyAdminsCanReadTheLog`, `CreateAndArchive_LeaveAnAuditTrail_WithTheActorAndTime`, E2E UI-9 |
| F-07 | **Fixed** | Global fixed-window limit per user (600/min, per address when anonymous) and an export limit (10/min per user); 429 ProblemDetails | `Exports_AreThrottledPerUser`, `ApiCalls_AreThrottledPerUser_WithoutAffectingOthers` |
| F-08 | **Fixed** | `LoginThrottle`: 5 failures per account-and-address or 20 per account lock for 15 minutes; forwarded headers are only honoured from configured proxies | `RepeatedWrongPasswords_LockTheAccount_EvenWithTheRightPassword` (includes the spoofed-address case), `LoginThrottleTests` (4) |
| F-09 | **Fixed** | Only `Qualified` leads convert; the Convert button is disabled otherwise | `OnlyQualifiedLeads_CanBeConverted`, `Only_a_qualified_lead_can_be_converted` |
| F-10 | **Fixed** | `IdempotencyFilter`: the first 2xx response per user, path and `Idempotency-Key` is replayed for 24 h; a different body with the same key → 422. The client sends a key per opened "new" form | `IdempotencyKey_MakesARetriedCreateReturnTheFirstResult` |
| F-11 | **Fixed** | Close and due dates must fall within the last 5 / next 10 years (injected clock) | `AbsurdDates_AreRejected` (4 cases) |
| F-12 | **Fixed** | `RequestSizeGuard` (Content-Length > 1 MB → 413, `Connection: close`) plus Kestrel `MaxRequestBodySize` for chunked bodies | `OversizedBody_IsRejectedBeforeParsing` |
| F-13 | **Fixed** | `UseHsts` (365 days, subdomains) and `UseHttpsRedirection` outside Development; Kestrel `AddServerHeader = false` | `HttpsResponses_CarryHsts`, probe CF-1 |
| F-14 | **Fixed** | No key in any settings file; Development creates a random key once in `App_Data/dev-signing.key` (git-ignored). Demo mode outside Development logs a warning at start-up | `NoSigningKeyIsCommittedInSettings`, `CreatesARandomKeyOnce_ThenReusesIt` |
| F-15 | **Fixed** | npm `overrides` pin the patched `glob` 14.0.3 and `minimatch` 11.0.4 under the Angular CLI (npm's own suggestion was a downgrade to CLI 16) | `npm audit` → 0 vulnerabilities, production build passes |
| F-16 | **Accepted** | Output encoding is the control (Angular, toast `titleText`, CSV escaping) | E2E UI-1 |
| F-17 | **Accepted** | The owner picker needs the team list | — |

### Found while retesting

| Issue | What happened | Fix | Proof |
|---|---|---|---|
| R-1 | The first version of the F-08 fix added the forwarded-headers middleware with empty trust lists, which made it accept `X-Forwarded-For` from anyone: a guesser could invent a new address per attempt (probe RE-5 → 200) | The middleware is only added when proxies are configured | The spoofed request in `RepeatedWrongPasswords_…` failed with the bug re-introduced (`Expected: TooManyRequests, Actual: OK`) and passes with the fix |
| R-2 | Rejected uploads left the connection open for reuse, so the next request on it was reset | 413 responses send `Connection: close` | `curl`: HTTP 413, `Connection: close` |
| R-3 | Each rejected duplicate was logged twice at Error with a full stack trace | A Serilog filter drops expected unique-constraint exceptions; the API logs one warning per conflict | Log after the probe run: no stack traces; one EF "Failed executing DbCommand" line per conflict remains (no parameter values) |

## Findings (as reported before remediation)

All findings are **Confirmed** (reproduced on this build) unless marked otherwise. Severity uses impact × likelihood for a small internal sales team.

| ID | Finding | Severity | Location | Proof (exact request or steps) | Impact | Recommended fix (not applied) |
|---|---|---|---|---|---|---|
| F-01 | A deactivated user keeps full API access until their token expires (up to 8 h). Only `/api/auth/me` checks `IsActive`. | **High** | `src/NexCrm.Api/Program.cs` (JwtBearer setup has no per-request user check); `AuthService.MeAsync` | Probe AU-3: sign in as `omer`, set `Users.IsActive = 0`, then `GET /api/dashboard` with the old token → **200**; `GET /api/auth/me` → 401 | A dismissed employee can keep reading the whole pipeline and contact book, and editing records, for a full working day | Validate the user on every request in `JwtBearerEvents.OnTokenValidated` (cached `IsActive` + a security-stamp claim), or move to 15-minute access tokens with a revocable refresh token |
| F-02 | No server-side sign-out or token revocation; 8-hour bearer tokens are held in `sessionStorage`. | **Medium** | `Program.cs` (stateless JWT), `client/src/app/core/auth.service.ts` (logout only clears storage), `Jwt:AccessTokenMinutes = 480` | Probe AU-2: call `/api/dashboard` with a token, "sign out", replay the same token → **200** | A stolen or shared token stays valid for hours; signing out of a shared computer does not end the session | Short-lived access token + `httpOnly`, `SameSite=Strict` rotating refresh cookie with a revocation list (the DineSync pattern); revoke on logout |
| F-03 | Horizontal access: any signed-in user can read, edit, move and re-own any record, including another rep's deals (BOLA). | **Medium** (product decision, see Q1) | All `CrudController` actions; `CrudService.SaveAsync` accepts any `ownerId` | Probe AC-2: as `sara`, `PUT /api/deals/367` (owner 2) with `amount: 1, ownerId: 3` → **200**, owner changed to sara | A rep can take credit for a colleague's deal or zero its value; no trail of who did it (F-06) | Decide Q1. Minimum: only managers may change `ownerId` on someone else's record; optionally scope writes to own records for sales reps |
| F-04 | Concurrent creates bypass the duplicate-company check (check-then-insert, no unique index). | **Medium** | `CompanyService.ValidateAsync`; `CompanyConfiguration` (index is not unique) | Probe BL-2: 50 parallel `POST /api/companies` with the same name → **201×8**, 409×42; 8 rows | Duplicate accounts split contacts and deals, and corrupt the reports | Unique index on the normalised name (`lower(Name)`, filtered `IsDeleted = 0`) and map the unique-constraint error to 409 |
| F-05 | Same race for contacts: duplicate emails under concurrency. | **Medium** | `ContactService.ValidateAsync`; `ContactConfiguration` (non-unique `Email` index) | Probe BL-3: 50 parallel `POST /api/contacts` with one email → **12** contacts | Duplicate people, double outreach, lead conversion matches the wrong one | Unique filtered index on `Email` where `IsDeleted = 0`; map to 409 |
| F-06 | No audit trail for sensitive actions (delete, convert, export, ownership change); request logs carry no user id. | **Medium** | `Program.cs` (`UseSerilogRequestLogging` without user enrichment); no audit table | Log after the probe run: `HTTP DELETE /api/deals/568 responded 204` with no user; exports and conversions are logged the same way | Cannot answer "who deleted this account?" or "who exported the contact book?"; weak incident response | An `AuditLog` table written by `CrudService` and the convert/export actions; enrich Serilog with `UserId` and `Username` |
| F-07 | No rate limiting outside sign-in; exports are unthrottled. | **Medium** | `Program.cs` (only the `login` policy exists) | Probe RE-4: 300 parallel `GET /api/dashboard` → 300 served, 0 throttled. Probe RE-3: 30 parallel `POST /api/deals/export` → all 200 | One account (or a stolen token) can scrape every record at full speed or load the server | Global per-user fixed-window limiter, and a strict export limiter (for example 10/min per user) |
| F-08 | Brute-force protection is per client address only; no per-account limit or lockout. | **Low** | `Program.cs` rate limiter partitioned by `RemoteIpAddress` | Probe RE-5: guesses against `admin` were throttled from attempt 16 of the minute, per address; nothing limits guesses for one account across addresses. `X-Forwarded-For` spoofing did **not** bypass it | Credential stuffing from many addresses is unthrottled; behind a reverse proxy every user shares one address and can lock each other out | Add a per-username limiter with progressive back-off; configure forwarded headers with known proxies before deploying behind one |
| F-09 | A lead can be converted without being qualified (status New or Contacted). | **Low** | `Lead.EnsureCanConvert` rejects only Converted and Unqualified | Probe BL-1: create a lead with `status: "New"`, `POST /api/leads/{id}/convert` → **200** | Unvetted leads inflate the pipeline and the forecast | Require `Qualified` (and say so in LD-3), or document that conversion is allowed from any open status |
| F-10 | Create requests are not idempotent; a replayed or double-submitted POST creates a second record. | **Low** | All `POST` create actions | Probe BL-5: two identical `POST /api/deals` → deals 567 and 568 | Duplicate deals and tasks after a network retry or a double click | Accept an `Idempotency-Key` header on POST and store the first response for 24 h |
| F-11 | Dates have no sensible range: a deal can close on 9999-12-31 and tasks can be due in year 1 or 9999. | **Low** | `SaveDealValidator`, `SaveTaskValidator` | Probe BL-6: `expectedCloseDate: "9999-12-31"` → **201** | Garbage dates break "closing in 30 days", the board's overdue flags and the trend | `InclusiveBetween(today − 5 years, today + 10 years)` for both dates |
| F-12 | The request body limit is Kestrel's default 30 MB; a 20 MB JSON body is read and parsed before validation rejects it. | **Low** | `Program.cs` (no `MaxRequestBodySize`) | Probe RE-1: 20 MB `notes` in `POST /api/tasks` → 400 after the body was fully parsed (126 ms) | Cheap memory and CPU pressure from a few requests | Set `Kestrel.Limits.MaxRequestBodySize` to about 1 MB (largest real request is a few KB) |
| F-13 | No HSTS or HTTPS redirect is configured for production, and the `Server: Kestrel` header is disclosed. | **Low** | `Program.cs` | Probe CF-1: response headers have no `Strict-Transport-Security`; `Server: Kestrel` | Downgrade risk if deployed without a TLS-enforcing proxy; minor fingerprinting | `UseHsts()` and `UseHttpsRedirection()` outside Development; `AddServerHeader = false` |
| F-14 | A JWT signing key is committed in `appsettings.Development.json`; Development also enables Swagger and demo accounts whose password is the username. | **Low** | `src/NexCrm.Api/appsettings.Development.json` | Read the file: `"SigningKey": "local-dev-only-signing-key-change-me-0123456789"`; `DemoMode: true` | If an instance is ever started with `ASPNETCORE_ENVIRONMENT=Development` on a reachable host, anyone can mint admin tokens with the public key | Keep the dev key in user-secrets, and refuse to start when the known dev key or demo mode is used on a non-loopback address |
| F-15 | Build-time npm advisories: 16 moderate (`glob` / `minimatch` ReDoS) in the Angular CLI tool chain. | **Low** | `client/package-lock.json` (dev dependencies only) | `npm audit` → 16 moderate; `npm audit --omit=dev` → 0 | Affects the developer machine and CI only, never the shipped bundle | Upgrade the Angular CLI when patched transitive versions land; do not use `npm audit fix --force` (it installs breaking versions) |
| F-16 | Stored script payloads are accepted and stored verbatim. | **Info** | Contact, lead, company, deal and task text fields | Probe XS-1: `firstName: "<svg onload=alert(1)>"` → 201. E2E UI-1: rendered as text; `onerror` never ran; no element carries the attribute | None today (Angular escapes output; toasts use `titleText` / `text`; CSV is formula-escaped). A future non-Angular consumer (email, PDF) must encode too | Keep output encoding as the control; add a note to the contributor guide |
| F-17 | Every signed-in user can list all team members' names and roles. | **Info** | `GET /api/lookups/owners` | Probe MA-2: 6 users with name and role returned to a sales rep | Expected for a team CRM's owner picker | Accept |

## Controls that held (Confirmed secure)

| Probe | Test | Result |
|---|---|---|
| AC-1 | All 32 Swagger paths called without a token | 401 everywhere except `GET /api/auth/config` and `POST /api/auth/login` |
| AC-3 | Sales rep `DELETE`; token with `role` edited to `Admin` | 403; tampered token 401 |
| AU-1 | `alg: none`, expired, garbage tokens; wrong signature and issuer (integration tests) | 401 for all; a correctly signed token is accepted (control test) |
| IN-1 | SQL payloads in search, sort field, column filters (keys and values) and rules | 200 with 0 matches; unknown fields ignored; table intact |
| IN-2 | CRLF / `Set-Cookie` / `../` in the export file name; formula in a column header | Name sanitised to letters, digits, space, `-`, `_`; header cell escaped as `'=` (the probe's own "VULNERABLE" label was a false positive on the text "Set-Cookie" inside the sanitised name) |
| MA-1 | Over-posting `id`, `closedAt`, `isDeleted`, `createdAt`, `weighted`, and `probability` on a Won deal | All ignored; server values used |
| BL-4 | 50 parallel conversions of one lead | Exactly 1 deal (fixed in Phase 2: transaction + concurrency token) |
| RE-2 | 5,000-level nested JSON | 400 (System.Text.Json depth limit) |
| CF-2 | CORS preflight from `https://evil.example` | No `Access-Control-Allow-Origin`; only the configured client origin is allowed |
| CF-3 | Error bodies | No stack traces; malformed JSON returns the parser path only. Swagger is Development-only (integration test proves 404 elsewhere) |
| Browser | Open redirect via `returnUrl` (`https://evil…`, `//evil…`, `javascript:`, `/\evil…`) | Always lands on `/dashboard` on our origin; no script ran |
| Browser | Stored and DOM XSS through names shown in grids, drawers and toasts (E2E UI-1) | Rendered as text |
| Logs | Passwords, tokens or bearer headers in `logs/*.log` | None found (0 matching lines) |

## Not applicable to this build

SSRF (the API makes no outbound HTTP calls), file uploads and path traversal (no file endpoints; the export name is sanitised), CSRF (bearer header, no auth cookies), session fixation (no server session), password reset (not implemented), tenant isolation (single team by design, A1), NoSQL / command / template injection (none used; Angular templates are compiled ahead of time).

## OWASP mapping

| ID | OWASP Top 10 (2021) | OWASP API Security Top 10 (2023) | OWASP ASVS 4.0.3 |
|---|---|---|---|
| F-01 | A07 Identification and Authentication Failures | API2 Broken Authentication | V3.3.1, V3.3.4 (session termination), V4.1.3 |
| F-02 | A07 | API2 | V3.3.1, V3.5.2, V3.2.3 (token storage) |
| F-03 | A01 Broken Access Control | API1 Broken Object Level Authorization, API3 Broken Object Property Level Authorization | V4.2.1 |
| F-04, F-05 | A04 Insecure Design | API6 Unrestricted Access to Sensitive Business Flows | V11.1.6 (concurrency), V11.1.2 |
| F-06 | A09 Security Logging and Monitoring Failures | API9 (inventory/monitoring) | V7.1.3, V7.1.4, V7.2.1 |
| F-07 | A04 | API4 Unrestricted Resource Consumption | V11.1.4 |
| F-08 | A07 | API2 | V2.2.1 |
| F-09 | A04 | API6 | V11.1.1 (sequential steps) |
| F-10 | A04 | API6 | V11.1.2 |
| F-11 | A03 Injection / input validation | API8 Security Misconfiguration (validation) | V5.1.4 |
| F-12 | A05 Security Misconfiguration | API4 | V12.1.1, V13.1.3 |
| F-13 | A05, A02 Cryptographic Failures | API8 | V9.1.1, V14.4.5, V14.3.3 |
| F-14 | A05, A02 | API8 | V2.10.4, V6.4.1, V14.1.3 |
| F-15 | A06 Vulnerable and Outdated Components | API8 | V14.2.1 |
| F-16 | A03 (XSS) | (none) | V5.3.3 |
| F-17 | A01 | API3 | V8.3.4 |
