# Scope of improvement

Separate from the security findings in [`security/SECURITY-REVIEW.md`](../../security/SECURITY-REVIEW.md). Each item is tagged **Must / Should / Could** and effort **S** (under half a day), **M** (one to two days) or **L** (more than two days). Measurements were taken on the seeded demo database (120 companies, 420 contacts, 560 leads, 560 deals, 640 tasks) on one laptop.

## Performance

| # | Item | Evidence | Priority | Effort |
|---|---|---|---|---|
| P-1 | The bell calls the full dashboard (24 SQL round trips) on **every route change** and every 60 s per open tab. Add a small `/api/alerts` endpoint (two counts and one list) and call it on a timer only. | `client/src/app/layout/shell.ts` lines 101–105 and 118; `DashboardService` has 24 awaited queries | **Should** | S |
| P-2 | Cache the dashboard for 30 s in `IMemoryCache`, keyed by day, invalidated on deal and task writes. Warm calls take 8 ms today, but they scale with users × navigations. | `curl` timings: 7.5–8.3 ms warm | Should | S |
| P-3 | Collapse the dashboard's KPI queries into two grouped queries (deals by stage and month, tasks by status) instead of 24 round trips. | `DashboardService.KpisAsync`: 13 queries alone | Should | M |
| P-4 | Global search ORs `lower(col)` over every text column, so no index can be used. That's fine at thousands of rows; at 100k+ add SQLite FTS5 for name, email and title. | `QueryEngine.ApplyFilters` search branch | Could | M |
| P-5 | The first export after start-up took 3.96 s (ClosedXML JIT and font metrics); warm exports take ~165 ms. Warm it up at start-up or skip `AdjustToContents` for large sheets. | Probe RE-3 vs warm `curl` timing | Could | S |
| P-6 | SQLite allows one writer at a time. WAL is already on (`journal_mode = wal`), so reads don't block. The Phase 7 load test should confirm write throughput before 1,000 users; PostgreSQL is the next step if it doesn't hold. | `pragma journal_mode` → `wal` | Should (verify in Phase 7) | — |

## Code design and maintainability

| # | Item | Priority | Effort |
|---|---|---|---|
| D-1 | ~~Unique indexes for company name and contact email~~ **Done** with security F-04 / F-05. | Must | S |
| D-2 | Optimistic concurrency on edits: two people editing the same deal is last-write-wins today. Send `updatedAt` back as an `If-Match` style token and return 409 on mismatch. | Should | M |
| D-3 | Two error shapes for 400: model-binding errors use ASP.NET's "One or more validation errors occurred." with `$.field` keys, while FluentValidation uses "Please fix the highlighted fields." with camelCase keys. Normalise in `InvalidModelStateResponseFactory`. | Should | S |
| D-4 | `CrudService.ValidateAsync`'s default is overridden by every feature; consider making it abstract so a new feature can't forget its checks. | Could | S |
| D-5 | The data table is a copy of DineSync's; extracting a shared Angular library would stop the two drifting apart. | Could | L |
| D-6 | `AppOptions.ClientOrigin` defaults to `http://localhost:4300` in code; production must set it explicitly. Fail fast when it's missing outside Development. | Should | S |

## Error handling and UX

| # | Item | Priority | Effort |
|---|---|---|---|
| U-1 | A 401 mid-edit signs the user out and discards the open drawer. Keep the draft in memory and restore it after signing in again (pairs with security F-02's refresh tokens). | Should | M |
| U-2 | Board drag and drop has no undo; a mis-drop to Lost is one confirm away from a wrong forecast. Add an "Undo" action in the toast. | Should | S |
| U-3 | Lookups show "No matches" without offering to create the company or contact inline. | Could | M |
| U-4 | Empty dashboard for a brand-new install (no data) shows zeros with no guidance; add a first-run card ("Import contacts / Add your first deal"). | Could | S |

## Accessibility (WCAG 2.2 AA)

| # | Item | Criterion | Priority | Effort |
|---|---|---|---|---|
| A-1 | Moving a deal on the board needs a pointer (drag). Keyboard users can only change the stage through the edit drawer. Add a "Move to…" menu on each card. | 2.1.1 Keyboard, 2.5.7 Dragging Movements | **Must** | S |
| A-2 | The drawer doesn't trap focus, doesn't move focus into the form on open, and doesn't return focus to the trigger on close. | 2.4.3 Focus Order, 2.4.11 Focus Not Obscured | **Must** | M |
| A-3 | Field errors are not linked to their inputs (`aria-describedby`, `aria-invalid`), so screen readers don't announce them on focus. | 3.3.1 Error Identification, 4.1.2 | Should | S |
| A-4 | Charts have no text alternative or data table fallback. | 1.1.1 Non-text Content | Should | S |
| A-5 | Error toasts vanish after 10 s; keyboard-only users cannot pause them (pause is on hover). Keep error toasts until dismissed, or pause on focus. | 2.2.1 Timing Adjustable | Should | S |
| A-6 | No "Skip to content" link before the sidebar. | 2.4.1 Bypass Blocks | Could | S |
| A-7 | Run an automated axe audit in E2E (needs one dev dependency, `axe-core`; ask first). | — | Could | S |

## Observability

| # | Item | Priority | Effort |
|---|---|---|---|
| O-1 | ~~Enrich Serilog with the user on every request~~ **Done** with security F-06 (`UserName`, `UserId`, `ClientIp`). | Must | S |
| O-2 | Log security events explicitly: failed sign-ins are now audited (F-06); 403s and rate-limit rejections are still only request-log lines. | Should | S |
| O-3 | Metrics: request rate, p95 latency and errors per endpoint, DB command time, GC, thread pool. OpenTelemetry with a Prometheus exporter, or `dotnet-counters` during Phase 7. | Should | M |
| O-4 | `/health` only checks the database; add a readiness check for free disk space (SQLite file growth) and report the app version. | Could | S |

## Remaining test gaps

| # | Gap | Priority | Effort |
|---|---|---|---|
| T-1 | Client component tests: `RecordPage`, `LookupSelect` (keyboard, debounce, label sync) and the `DataTable` query building have no unit tests; only the toast helper does. Angular's test runner is configured (`ng test`) but unused. | Should | M |
| T-2 | Board drag and drop is verified by hand and by the screenshot run, not by an automated E2E assertion (HTML5 DnD needs CDP `Input.dispatchDragEvent`). | Should | S |
| T-3 | `DemoDataSeeder` (Infrastructure, ~250 lines) has no tests: determinism (same seed, same data) and invariants (no deal closes before it is created). | Could | S |
| T-4 | No mobile-viewport or axe accessibility checks in E2E (see A-7). | Could | S |
| T-5 | Load and soak behaviour is unknown until Phase 7. | **Must** (Phase 7) | M |
