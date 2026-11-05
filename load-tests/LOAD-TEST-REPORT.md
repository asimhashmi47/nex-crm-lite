# Load test: NexCRM Lite (Phase 7, baseline)

**Status:** baseline measured, root causes found, fixes **not yet applied** (Phase 8 needs approval).

## Setup

| | |
|---|---|
| Build | `dotnet publish -c Release`, `ASPNETCORE_ENVIRONMENT=Production`, no debugger, Kestrel on `http://localhost:5190` |
| Data | `seed --scale 20 --load-users 4000`: 2,400 companies, 8,400 contacts, 11,200 leads, 11,200 deals, 12,800 tasks (≈ 46,000 rows), plus 4,006 users |
| Users | One account per virtual user (`load0001`…); each signs in once and keeps its session |
| Behaviour | 3–8 s think time between actions; ≈ 80 % reads (board, grids, search, record view, lookups, dashboard) and ≈ 20 % writes (create task / contact / lead, move deal, complete task, edit company). Like the real client, page views also call `/api/dashboard` for the bell |
| Tool | [`crm-load.mjs`](crm-load.mjs), Node 22, no dependencies (no k6, NBomber or dotnet-counters on this machine; none were installed without asking) |
| Server counters | `GET /api/ops/runtime` (admin only, `Diagnostics:Enabled`): process CPU, working set, GC heap and pauses, thread-pool threads and queue, lock contention, and per-statement SQL timings (timed until the reader is disposed) |
| Machine | One laptop: Intel i5-8250U, 4 cores / 8 threads, 16 GB. **The load generator runs on the same machine** (no second machine available), so it competes for CPU |
| Deviations | Sign-in and per-user rate limits raised (`RateLimiting__LoginPerMinute`, `__RequestsPerMinute` = 1,000,000): every virtual user comes from one address, which the per-address limits would otherwise throttle. The limits themselves are proven by the integration tests |
| Thresholds | p95 < 500 ms, p99 < 1.5 s, errors < 1 % |

## Results (baseline)

| Profile | Users | Requests | Throughput | p50 | p95 | p99 | Errors | Verdict |
|---|---|---|---|---|---|---|---|---|
| Smoke | 10 | 65 | 0.8/s | 169 ms | **19.4 s** | 20.9 s | 0 % | **Fail** (latency) |
| Load (ramp 5 min, hold 10 min) | 1,000 | 164,358* | 129/s* | 955 ms | 30.0 s | 30.8 s | 99.8 %* | **Fail** |
| Spike (0 → 1,000 in 30 s, hold 2.5 min) | 1,000 | 10,128 | 42.6/s | 14 ms | 30.1 s | 30.2 s | **97.9 %** | **Fail** |
| Stress | — | — | — | — | — | — | — | Not run: the baseline already fails at 1 user (below) |
| Soak | — | — | — | — | — | — | — | Not run on the baseline; planned after the fixes |

\* The first load run exposed a flaw in the generator: a virtual user whose sign-in timed out was restarted 250 ms later without back-off, producing 150,704 sign-in attempts and inflating both the request count and the collapse. The generator now backs off exponentially (5 s → 60 s), and the spike run was repeated with that fix and with error codes recorded.

Spike baseline, by failure type: **8,167 `ECONNREFUSED`**, **1,751 timeouts (30 s)**, 210 successes. Server process CPU averaged **4.2 cores** (of 8 logical).

### Per endpoint, smoke (10 users): one endpoint is the problem

| Endpoint | p50 | p95 |
|---|---|---|
| `GET /dashboard` | 18.8 s | 20.9 s |
| `GET /dashboard` (bell) | 16.0 s | 19.2 s |
| `POST /auth/login` | 219 ms | 7.2 s (queued behind dashboards) |
| `POST /contacts/query` (search) | 204 ms | 223 ms |
| `GET /deals/board` | 52 ms | 135 ms |
| `PATCH /deals/{id}/stage`, `POST /tasks`, `PATCH /tasks/{id}/done` | 11–17 ms | 118–374 ms |
| `GET /deals/{id}` | 10 ms | 34 ms |

On an idle server, a single `GET /dashboard` takes **5.9 s and 5.9 s of CPU**; the server's idle CPU is 0.05 s per 10 s.

## 1. What held up

- Every endpoint except the dashboard, at 10 users: board, grids, search, record views and all writes stayed within thresholds.
- Memory: working set peaked at 420 MB, GC heap 60–165 MB, total GC pause 3.5 s over 20 minutes at 1,000 users. No leak pattern (the heap returned to ~60 MB between bursts).
- The SQLite write path: audit inserts averaged 4.6 ms and login updates 4.5 ms even while the server was saturated.
- Raw connection handling: an idle server accepted **2,000 concurrent connections** cleanly (all 200 in 3.2 s).

## 2. What broke, in order

| # | Symptom | Load where it started |
|---|---|---|
| 1 | `GET /dashboard` p95 above threshold | **1 user**: 5.9 s per call on an idle server |
| 2 | Every other endpoint slows as dashboards occupy the CPU and threads (sign-in p95 7.2 s) | 10 users |
| 3 | Thread-pool queue grows (112 → 2,000–5,000 work items); threads creep from 12 to 55; lock contention 4 → 2,011 | ≈ 100–200 users |
| 4 | Requests time out at 30 s | ≈ 200 users |
| 5 | **Connections refused** (`ECONNREFUSED`): Kestrel cannot accept fast enough and the OS backlog overflows | ≈ 400 users, and immediately in the spike |

## 3. Root causes

| # | Cause | Evidence |
|---|---|---|
| R1 | **Correlated sub-queries over every user (missing aggregate / index use).** The leaderboard selects all 4,006 active users and runs three correlated `SUM`/`COUNT` sub-queries per user. SQLite resolves each with `IX_Deals_Stage` (≈ 3,700 Won rows), not the owner index: about 15 million row visits per column. The design is also unbounded: it returns a row for every user | Query plan: `SCAN u` → `CORRELATED SCALAR SUBQUERY` → `SEARCH d USING INDEX IX_Deals_Stage`. One column alone: 3.06 s. Under load: 221 runs, mean **48.6 s**, max **104 s**, 10,734 s of database time |
| R2 | **Synchronous I/O blocks the thread pool.** Microsoft.Data.Sqlite's `*Async` methods complete synchronously, so a 6–100 s query holds a thread-pool thread for its whole length. The pool grows by only a thread or two per second, so Kestrel's accept loop and every other request starve | Pool queue 2,000–5,000 with 12–55 threads; `ECONNREFUSED` only under load |
| R3 | **No caching of team-wide data.** The dashboard is the same for every user, yet it is computed per request, and the client asks for it on every page view (bell) and every 60 s per tab. It is ≈ 40 % of all requests in this mix | Request counts per endpoint; P-1 in the improvement backlog |
| R4 | **Unbounded in-memory aggregation.** The trend loads every lead created in the last 12 months (11,200 rows at this scale) into memory on each dashboard call to group them by month | `SELECT "l"."CreatedAt" FROM "Leads"…`: 240 runs, mean 218 ms, max 1 s under load |
| R5 | **Sign-in is CPU-heavy by design** (PBKDF2-SHA512, 100,000 iterations). Healthy, but a 1,000-user spike costs a few CPU-seconds of hashing | Sign-in p50 219 ms at 10 users |

## 4. Fix plan (ranked by impact ÷ effort)

| Rank | Fix | Effort | Expected improvement |
|---|---|---|---|
| 1 | **Rewrite the leaderboard** as one grouped query over `Deals` (by owner, won in the window, open pipeline), top 10 only, joined to user names; add a composite index `(OwnerId, Stage, ClosedAt)` | S | Dashboard 5.9 s → tens of ms; removes R1, which triggers everything else |
| 2 | **Cache the dashboard for 30 s** (team-wide, one computation per 30 s for all users) | S | Dashboard cost becomes independent of user count |
| 3 | **Give the bell its own light endpoint** (`/api/alerts`: three counts and the closing-soon count) and stop calling it on every route change; keep the 60 s poll | S | ≈ 40 % fewer requests; the bell no longer depends on the heaviest endpoint |
| 4 | **Aggregate the trend in SQL** (group by month) instead of loading 11,200 rows | S | Removes R4: ≈ 200 ms → a few ms under load |
| 5 | **Raise the thread-pool minimum** (e.g. 4× cores) so short synchronous SQLite calls don't queue behind each other during bursts | S | Mitigates R2 during spikes; no effect once queries are short |
| 6 | Re-measure, then decide on a **15 s cache for the per-request session check** (one indexed query per request today; deactivation and sign-out would take effect within 15 s instead of at once, a trade-off for you to accept) | S | Saves one query per request; only if the re-test shows it matters |

After fixes 1–5, rerun smoke → load → spike → stress (stepping past 1,000 until thresholds break) → soak (1 hour at the capacity found), and compare with this baseline.

## 5. Capacity estimate (baseline)

- **Within thresholds: 0 users.** A single user's dashboard call takes 5.9 s, ten times the p95 budget.
- **Before outright failure:** the server keeps answering lighter requests up to roughly 100–200 concurrent users with these think times. Beyond ≈ 400 it starts refusing connections.

## Reproduce

```bash
dotnet publish src/NexCrm.Api -c Release -o <dir>
```

From `<dir>`, with `ASPNETCORE_ENVIRONMENT=Production`, `Jwt__SigningKey` and `ConnectionStrings__Default` set:

```bash
LOADTEST_PASSWORD=<test password> ./NexCrm.Api seed --scale 20 --load-users 4000
```

Run the API with `Diagnostics__Enabled=true` and the raised rate limits above, then:

```bash
LOADTEST_PASSWORD=<test password> node load-tests/crm-load.mjs smoke
```

Profiles: `smoke`, `load`, `spike`, `stress`, `soak` (`--soak-users`, `--soak-seconds`). Results are saved as JSON in `load-tests/results/`.
