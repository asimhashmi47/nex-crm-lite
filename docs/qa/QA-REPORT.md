# QA report: phases 0–5 and 8

## Phase 0: criteria

[ACCEPTANCE-CRITERIA.md](ACCEPTANCE-CRITERIA.md): 54 numbered criteria across auth, permissions, the five record types, grids and export, dashboard, lookups, cross-cutting concerns and the client. Every criterion is tagged on at least one test (`[Trait("AC", "…")]` in .NET, the test title in E2E).

## Phases 1–2: tests

| Suite | Tool | Tests | Result |
|---|---|---|---|
| Unit: domain rules, validators, services on real in-memory SQLite, query engine, export, dashboard | xUnit | 183 | **183 passed** |
| Integration: full HTTP pipeline, real SQLite file per class, migrations, auth, validation, serialization, DB state | xUnit + `WebApplicationFactory` | 46 | **46 passed** (4 consecutive runs) |
| Client unit | `node:test` | 3 | **3 passed** |
| E2E journeys: sign-in redirect, dashboard → board, create contact with validation, toast and XSS, convert lead, complete task, delete rights | `node:test` + Chrome DevTools Protocol | 7 | **7 passed** |

### Coverage

| Scope | Line | Branch |
|---|---|---|
| Business rules and auth (Domain entities, feature services, `CrudService`, `AuthService`) | **99.8 %** (1126/1128) | **99.1 %** (218/220) |
| `NexCrm.Domain` (unit run) | 99.2 % | 100 % |
| `NexCrm.Application` (unit run) | 97.5 % | 91.2 % |
| `NexCrm.Api` (integration run) | 82.7 % | 62.8 % |
| `NexCrm.Infrastructure` (integration run) | 88.5 % | 23.6 % |

The two uncovered business-rule lines are `CrmTask.Deal`'s getter, which only EF expression trees read. The two uncovered branches are compiler-generated `async` state-machine paths. Infrastructure branch coverage is low because `DemoDataSeeder` is not exercised by the tests (gap T-3).

### Defects the tests found, and fixed

| # | Defect | Found by | Fix |
|---|---|---|---|
| 1 | Synchronous `SaveChanges()` skipped the `CreatedAt` / `UpdatedAt` stamps (rows saved as `0001-01-01`) | `ColumnFilters_FollowTheirSyntax(createdAt…)` | Stamp in both save paths (`AppDbContext.StampTimes`) |
| 2 | Company names were unique only with the same case | `Create_DuplicateNameIgnoringCase_IsRefused` | Compare `lower(Name)` |
| 3 | Lead conversion didn't reuse a company whose name differed in case | `Convert_ReusesCompanyByNameIgnoringCase_AndContactByEmail` | Same normalisation |
| 4 | Deals without a close date and tasks without a due date were accepted | `Deal_MissingCloseDate_IsRejected`, `Task_MissingDueDate_IsRejected` | `NotEmpty` rules |
| 5 | Grid search and text filters were case-sensitive on SQLite (`instr`) | `GlobalSearch_IsCaseInsensitive_AndTreatsWildcardsLiterally` | Lower both sides in `QueryEngine.StringOp` |
| 6 | Picker lookups were case-sensitive | `Deals_FilterByTitle_NewestFirst` | Lower both sides in `LookupService` |
| 7 | Handled errors returned `application/json` instead of `application/problem+json` | `Login_BadOrInactive_Returns401ProblemWithoutDetail` | Content type set in `ProblemExceptionHandler` |
| 8 | 50 parallel conversions of one lead created 7 deals | `Lead_Converted50TimesInParallel_CreatesExactlyOneDeal` | Transaction around convert + `Status` concurrency token (migration `LeadStatusConcurrencyToken`) |
| 9 | `PATCH /stage` with `{}` moved the deal back to Prospecting | `MoveDeal_InvalidStage_Returns400("{}")` | `DealStage?` + `NotNull` rule |

Also removed: unused `Contact.FullName`. Test seams added: `Database:SeedDemoData` setting, and the request logger now uses each host's own Serilog logger.

## Phase 3: quality gate (all run, all green)

| Gate | Command | Result |
|---|---|---|
| .NET release build | `dotnet build NexCrm.slnx -c Release` | 0 warnings, 0 errors (warnings are errors) |
| .NET tests + coverage | `dotnet test NexCrm.slnx -c Release --collect:"XPlat Code Coverage"` | 183 + 46 passed |
| C# lint | `dotnet format NexCrm.slnx --verify-no-changes` | exit 0 (after applying the formatter once) |
| TS lint | `npx prettier --check "src/**/*.{ts,html,css}"` | all files pass (after applying the project's `.prettierrc` once) |
| Type check | `npx tsc -p tsconfig.app.json --noEmit` | exit 0 |
| Client tests | `npm run test:unit` | 3 passed |
| Production build | `npx ng build` | complete, 124.8 kB initial transfer |
| E2E | `node --test e2e/journeys.test.mjs` | 7 passed |
| .NET CVEs | `dotnet list package --vulnerable --include-transitive` | none in any project |
| npm CVEs, shipped | `npm audit --omit=dev` | 0 |
| npm CVEs, build tooling | `npm audit` | 16 moderate (`glob` / `minimatch`), dev-only; recorded as F-15, not force-fixed |

## Phase 4: security

[security/SECURITY-REVIEW.md](../../security/SECURITY-REVIEW.md): 17 findings (1 High, 6 Medium, 8 Low, 2 Info), 13 controls confirmed secure, OWASP Top 10 / API Top 10 / ASVS mapping. No code changed in that phase.

## Phase 5: improvements

[IMPROVEMENTS.md](IMPROVEMENTS.md): performance, design, UX, accessibility, observability and test gaps, each tagged Must/Should/Could with effort.

## How to rerun everything

```bash
dotnet test NexCrm.slnx -c Release
```

```bash
cd client && npm run test:unit
```

With the API and client running on demo data:

```bash
node --test e2e/journeys.test.mjs
```

```bash
node --experimental-sqlite security/pentest-probes.mjs
```

## Phase 8: fixes (all findings)

Each finding got a failing test first (18 new HTTP-level tests, all red for the behaviour they describe), then the fix, then the full suite and the original attack. Details, before/after numbers and test names per finding: [security/SECURITY-REVIEW.md](../../security/SECURITY-REVIEW.md#status-after-remediation-phase-8).

| | Before Phase 8 | After |
|---|---|---|
| Pentest probes needing attention | 14 of 25 | **0 of 25** |
| Unit tests | 183 | **201** |
| Integration tests | 46 | **64** |
| E2E journeys | 7 | **9** |
| Business-rule + auth coverage (unit) | 99.8 % line / 99.1 % branch | 98.8 % line / 97.9 % branch (the remaining lines are EF navigation getters; the new auth and audit code is covered) |
| npm audit (all, incl. build tooling) | 16 moderate | **0** |

New migration `SecurityHardening` adds `Users.SessionVersion`, `Companies.NameKey`, the `AuditLog` table and two unique indexes. It archives existing duplicates first; it was run on the dev database that held the pentest's duplicates (1 company name group, 1 email group → 0 after, all indexes created).

Commits: git has not been initialised in this folder (declined earlier), so the "one fix per commit" rule could not be followed. Each fix is listed separately in the security review.

### Final quality gate

| Gate | Result |
|---|---|
| `dotnet build NexCrm.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet test NexCrm.slnx -c Release` | 201 + 64 passed |
| `dotnet format --verify-no-changes` | exit 0 |
| `prettier --check` | all files pass |
| `tsc --noEmit` | exit 0 |
| `npm run test:unit` | 3 passed |
| `ng build` | complete, 126.6 kB initial transfer |
| `node --test e2e/journeys.test.mjs` | 9 passed |
| `dotnet list package --vulnerable --include-transitive` | none |
| `npm audit` / `npm audit --omit=dev` | 0 / 0 |
| `node --experimental-sqlite security/pentest-probes.mjs` | 25 probes, 0 needing attention |

### Remaining risks

- **Q1 is only half answered.** Reps can still edit any team record (by design today); only ownership changes are restricted.
- **One instance only.** The sign-in lockout, idempotency cache and rate limits live in memory. Running two API instances would need a shared store (Redis or the database).
- **SQLite serialises writes.** Under 50 simultaneous inserts each waited 1–4 s for the write lock. The Phase 7 load test will measure where this breaks.
- **Session check costs one indexed query per request.** Cheap here; Phase 7 will show whether it needs a short cache.
- **Log noise:** EF still writes one Error line (SQL text, no values, no stack) for each rejected duplicate.
- Improvements not covered by findings stay in [IMPROVEMENTS.md](IMPROVEMENTS.md) (accessibility Musts A-1 and A-2, P-1 bell polling, T-1 client component tests).
