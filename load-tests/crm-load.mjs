// Load generator for NexCRM Lite: many unique users, realistic think time and read/write mix. No dependencies.
// Run against a production-like instance (Release build, seeded with --scale and --load-users), never production.
//
//   node load-tests/crm-load.mjs <profile> [--users 1000] [--base http://localhost:5190] [--out load-tests/results]
//   profiles: smoke | load | spike | stress | soak
//
// Environment: LOADTEST_PASSWORD (password of load0001…), ADMIN_PASSWORD (for the runtime counters, default "admin").
import fs from 'node:fs';
import path from 'node:path';
import { monitorEventLoopDelay } from 'node:perf_hooks';
import { setTimeout as sleep } from 'node:timers/promises';

const argv = process.argv.slice(2);
const opt = (name, fallback) => { const i = argv.indexOf(`--${name}`); return i >= 0 ? argv[i + 1] : fallback; };
const PROFILE = argv[0] ?? 'smoke';
const BASE = opt('base', 'http://localhost:5190');
const MAX_USERS = Number(opt('users', 4000));
const OUT = opt('out', 'load-tests/results');
const PASSWORD = process.env.LOADTEST_PASSWORD;
if (!PASSWORD) throw new Error('Set LOADTEST_PASSWORD to the load users\' password.');
if (!/^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(BASE)) throw new Error('Load tests only run against localhost.');

// Thresholds from the brief: p95 < 500 ms, p99 < 1.5 s, errors < 1 %.
const THRESHOLDS = { p95: 500, p99: 1500, errorRate: 0.01 };

// Stages: ramp linearly to `target` users over `seconds`.
const PROFILES = {
  smoke: [{ seconds: 10, target: 10 }, { seconds: 50, target: 10 }],
  load: [{ seconds: 300, target: 1000 }, { seconds: 600, target: 1000 }, { seconds: 30, target: 0 }],
  spike: [{ seconds: 30, target: 1000 }, { seconds: 150, target: 1000 }, { seconds: 30, target: 0 }],
  // Steps past 1,000 until thresholds break; stops early once two steps in a row are broken.
  stress: [100, 200, 300, 400, 600, 800, 1000, 1500, 2000].flatMap((t) => [{ seconds: 45, target: t, step: t }, { seconds: 120, target: t, step: t }]),
  soak: [{ seconds: 120, target: Number(opt('soak-users', 1000)) }, { seconds: Number(opt('soak-seconds', 3600)), target: Number(opt('soak-users', 1000)) }],
};
const stages = PROFILES[PROFILE];
if (!stages) throw new Error(`Unknown profile ${PROFILE}`);

// ── Metrics ───────────────────────────────────────────────────────────────────────────────────────

const started = Date.now();
const metrics = new Map();       // endpoint → { lat: [], statuses: {}, errors: n }
const timeline = [];             // one point every 5 s
const samples = [];              // server runtime counters
let windowCount = 0;
let windowErrors = 0;
let windowLat = [];
const loopDelay = monitorEventLoopDelay({ resolution: 20 });
loopDelay.enable();

function note(name, ms, status) {
  let m = metrics.get(name);
  if (!m) metrics.set(name, (m = { lat: [], statuses: {}, errors: 0, windows: new Map() }));
  m.lat.push(ms);
  m.statuses[status] = (m.statuses[status] ?? 0) + 1;
  const failed = typeof status === 'string' || status >= 500 || status === 429 || status === 401;
  if (failed) m.errors++;
  windowCount++;
  if (failed) windowErrors++;
  windowLat.push(ms);
}

const pct = (sorted, p) => (sorted.length ? sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))] : 0);

async function call(name, method, url, { token, body, headers = {} } = {}) {
  const t = performance.now();
  try {
    const res = await fetch(BASE + url, {
      method,
      headers: { ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers },
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal: AbortSignal.timeout(30_000),
    });
    const text = await res.text();
    note(name, performance.now() - t, res.status);
    return { status: res.status, json: text && res.headers.get('content-type')?.includes('json') ? JSON.parse(text) : undefined };
  } catch (e) {
    // Keep the reason: a 30 s timeout and a refused connection are different failures.
    const code = e?.name === 'TimeoutError' ? 'TIMEOUT' : `ERR:${e?.cause?.code ?? e?.cause?.name ?? e?.name ?? 'unknown'}`;
    note(name, performance.now() - t, code);
    return { status: code };
  }
}

// ── Shared reference data (ids to browse and edit) ────────────────────────────────────────────────

const pick = (a) => a[Math.floor(Math.random() * a.length)];
const between = (lo, hi) => lo + Math.random() * (hi - lo);
const STAGES = ['Prospecting', 'Qualification', 'Proposal', 'Negotiation'];
const TERMS = ['an', 'son', 'llc', 'group', 'ali', 'sara', 'tech', 'inc', 'data', 'pilot'];

async function adminToken() {
  const r = await fetch(`${BASE}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username: 'admin', password: process.env.ADMIN_PASSWORD ?? 'admin' }) });
  return (await r.json()).accessToken;
}

async function ids(token, resource, filters = {}) {
  const r = await fetch(`${BASE}/api/${resource}/query`, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }, body: JSON.stringify({ pageSize: 500, columnFilters: filters }) });
  return (await r.json()).items.map((x) => x.id);
}

const admin = await adminToken();
const ref = {
  deals: await ids(admin, 'deals', { stage: 'Prospecting,Qualification,Proposal,Negotiation' }),
  companies: await ids(admin, 'companies'),
  contacts: await ids(admin, 'contacts'),
  tasks: await ids(admin, 'tasks', { isDone: 'false' }),
};
const owner = async (token) => JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString()).sub;

// ── One virtual user ──────────────────────────────────────────────────────────────────────────────

// Weighted actions: ~80 % reads, ~20 % writes. The bell is modelled separately: one /api/dashboard/alerts call per user per minute,
// as the shell does since load fix 3. Pass --old-bell to model the earlier client (full dashboard on every page view).
const ACTIONS = [
  [16, 'board', async (u) => { await call('GET /deals/board', 'GET', '/api/deals/board', u); await oldBell(u); }],
  [10, 'dashboard', async (u) => call('GET /dashboard', 'GET', '/api/dashboard', u)],
  [22, 'list', async (u) => {
    const resource = pick(['deals', 'contacts', 'leads', 'companies', 'tasks']);
    await call(`POST /${resource}/query`, 'POST', `/api/${resource}/query`, { ...u, body: { page: 1 + Math.floor(Math.random() * 3), pageSize: 25, sortBy: 'createdAt', sortDesc: true } });
    await oldBell(u);
  }],
  [12, 'search', async (u) => call('POST /contacts/query (search)', 'POST', '/api/contacts/query', { ...u, body: { pageSize: 25, search: pick(TERMS) } })],
  [8, 'open deal', async (u) => call('GET /deals/{id}', 'GET', `/api/deals/${pick(ref.deals)}`, u)],
  [6, 'lookup', async (u) => call('GET /lookups/companies', 'GET', `/api/lookups/companies?term=${pick(TERMS)}`, u)],
  [7, 'create task', async (u) => call('POST /tasks', 'POST', '/api/tasks', {
    ...u, headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: { subject: `Follow up ${Date.now()}`, type: pick(['Call', 'Email', 'Meeting', 'FollowUp']), priority: 'Normal', dueAt: new Date(Date.now() + 86_400_000 * between(1, 14)).toISOString().slice(0, 16), contactId: pick(ref.contacts), ownerId: u.id },
  })],
  [6, 'move deal', async (u) => call('PATCH /deals/{id}/stage', 'PATCH', `/api/deals/${pick(ref.deals)}/stage`, { ...u, body: { stage: pick(STAGES) } })],
  [5, 'complete task', async (u) => call('PATCH /tasks/{id}/done', 'PATCH', `/api/tasks/${pick(ref.tasks)}/done`, { ...u, body: { done: Math.random() < 0.7 } })],
  [3, 'create contact', async (u) => call('POST /contacts', 'POST', '/api/contacts', {
    ...u, headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: { firstName: 'Load', lastName: `User ${u.n}`, email: `vu${u.n}.${Date.now()}.${Math.floor(Math.random() * 1e6)}@load.test`, stage: 'Lead', ownerId: u.id },
  })],
  [3, 'create lead', async (u) => call('POST /leads', 'POST', '/api/leads', {
    ...u, headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: { name: `Lead ${u.n}`, companyName: `Load Co ${u.n}`, email: `lead${u.n}.${Date.now()}@load.test`, source: 'Website', status: 'New', score: 50, estimatedValue: 5000, ownerId: u.id },
  })],
  [2, 'edit company', async (u) => {
    const id = pick(ref.companies);
    const c = await call('GET /companies/{id}', 'GET', `/api/companies/${id}`, u);
    if (c.status !== 200) return;
    const x = c.json;
    await call('PUT /companies/{id}', 'PUT', `/api/companies/${id}`, { ...u, body: { name: x.name, industry: x.industry, website: x.website, phone: x.phone, city: x.city, country: x.country, employees: x.employees + 1, annualRevenue: x.annualRevenue, ownerId: x.ownerId } });
  }],
];
const OLD_BELL = argv.includes('--old-bell');
async function oldBell(u) { if (OLD_BELL) await call('GET /dashboard (bell)', 'GET', '/api/dashboard', u); }

const WEIGHT = ACTIONS.reduce((s, [w]) => s + w, 0);
const chooseAction = () => { let r = Math.random() * WEIGHT; for (const a of ACTIONS) { if ((r -= a[0]) < 0) return a; } return ACTIONS[0]; };

let active = 0;
let wanted = 0;
const running = new Set();

async function virtualUser(n) {
  active++;
  try {
    const username = `load${String(n).padStart(4, '0')}`;
    // A person whose sign-in fails waits and tries again, backing off; they don't hammer the button.
    let login;
    for (let attempt = 0; n <= wanted; attempt++) {
      login = await call('POST /auth/login', 'POST', '/api/auth/login', { body: { username, password: PASSWORD } });
      if (login.status === 200) break;
      await sleep(Math.min(60_000, 5000 * 2 ** attempt) * between(0.5, 1));
    }
    if (login?.status !== 200) return;
    const u = { n, token: login.json.accessToken, id: Number(await owner(login.json.accessToken)) };
    await call('GET /dashboard', 'GET', '/api/dashboard', u);
    let nextBell = Date.now() + between(0, 60_000);
    while (n <= wanted) {
      await sleep(between(3000, 8000));
      if (!OLD_BELL && Date.now() >= nextBell) { await call('GET /dashboard/alerts (bell)', 'GET', '/api/dashboard/alerts', u); nextBell += 60_000; }
      if (n > wanted) break;
      await chooseAction()[2](u);
    }
  } finally {
    active--;
    running.delete(n);
  }
}

// ── Server counters every 5 s ─────────────────────────────────────────────────────────────────────

async function sampleServer() {
  try {
    const r = await fetch(`${BASE}/api/ops/runtime`, { headers: { Authorization: `Bearer ${admin}` } });
    if (r.ok) samples.push({ t: (Date.now() - started) / 1000, ...(await r.json()) });
  } catch { /* the server may be saturated; the gap shows in the timeline */ }
}

// ── Driver ────────────────────────────────────────────────────────────────────────────────────────

console.log(`${PROFILE}: ${stages.length} stages against ${BASE}; reference data: ${ref.deals.length} open deals, ${ref.companies.length} companies, ${ref.contacts.length} contacts, ${ref.tasks.length} open tasks`);
await call('GET /dashboard (warm-up)', 'GET', '/api/dashboard', { token: admin });
metrics.clear();

let lastTick = Date.now();
let brokenSteps = 0;
const stepResults = [];
const ticker = setInterval(async () => {
  const sorted = windowLat.sort((a, b) => a - b);
  const point = {
    t: Math.round((Date.now() - started) / 1000), users: active, wanted, rps: +(windowCount / ((Date.now() - lastTick) / 1000)).toFixed(1),
    p50: Math.round(pct(sorted, 50)), p95: Math.round(pct(sorted, 95)), p99: Math.round(pct(sorted, 99)),
    errorRate: windowCount ? +(windowErrors / windowCount).toFixed(4) : 0,
    loopLagMs: Math.round(loopDelay.percentile(99) / 1e6), loadgenCpuS: +(process.cpuUsage().user / 1e6).toFixed(1),
  };
  timeline.push(point);
  windowCount = 0; windowErrors = 0; windowLat = []; lastTick = Date.now(); loopDelay.reset();
  if (timeline.length % 6 === 0) console.log(JSON.stringify(point));
  await sampleServer();
}, 5000);

for (const stage of stages) {
  const from = wanted;
  const stageStart = Date.now();
  while (Date.now() - stageStart < stage.seconds * 1000) {
    const progress = Math.min(1, (Date.now() - stageStart) / (stage.seconds * 1000));
    wanted = Math.round(from + (stage.target - from) * progress);
    // Every virtual user is its own account (load0001…), so sessions, limits and audit rows are per person.
    for (let n = 1; n <= Math.min(wanted, MAX_USERS); n++) {
      if (!running.has(n)) { running.add(n); virtualUser(n).catch(() => {}); }
    }
    await sleep(250);
  }
  wanted = stage.target;

  if (PROFILE === 'stress' && stage.seconds === 120) {
    const recent = timeline.slice(-24);
    const p95 = Math.max(...recent.map((p) => p.p95));
    const err = recent.reduce((s, p) => s + p.errorRate, 0) / Math.max(1, recent.length);
    const broken = p95 > THRESHOLDS.p95 || err > THRESHOLDS.errorRate;
    stepResults.push({ users: stage.target, worstWindowP95: p95, meanErrorRate: +err.toFixed(4), broken });
    console.log(`step ${stage.target} users: worst 5 s p95 ${p95} ms, error rate ${(err * 100).toFixed(2)} % → ${broken ? 'BROKEN' : 'ok'}`);
    brokenSteps = broken ? brokenSteps + 1 : 0;
    if (brokenSteps >= 2) break;
  }
}
wanted = 0;
const drainUntil = Date.now() + 15_000;
while (active > 0 && Date.now() < drainUntil) await sleep(250);
clearInterval(ticker);
await sampleServer();

// ── Report ────────────────────────────────────────────────────────────────────────────────────────

const endpoints = [...metrics.entries()].map(([name, m]) => {
  const s = m.lat.sort((a, b) => a - b);
  return {
    endpoint: name, requests: s.length, p50: Math.round(pct(s, 50)), p95: Math.round(pct(s, 95)), p99: Math.round(pct(s, 99)), max: Math.round(s.at(-1) ?? 0),
    errorRate: +(m.errors / Math.max(1, s.length)).toFixed(4), statuses: m.statuses,
  };
}).sort((a, b) => b.requests - a.requests);
const all = [...metrics.values()].flatMap((m) => m.lat).sort((a, b) => a - b);
const errors = [...metrics.values()].reduce((s, m) => s + m.errors, 0);
const seconds = (Date.now() - started) / 1000;
const summary = {
  profile: PROFILE, base: BASE, durationSeconds: Math.round(seconds), maxUsers: Math.max(...timeline.map((p) => p.users), 0),
  requests: all.length, rps: +(all.length / seconds).toFixed(1),
  p50: Math.round(pct(all, 50)), p95: Math.round(pct(all, 95)), p99: Math.round(pct(all, 99)), errorRate: +(errors / Math.max(1, all.length)).toFixed(4),
  thresholds: THRESHOLDS,
  passed: pct(all, 95) < THRESHOLDS.p95 && pct(all, 99) < THRESHOLDS.p99 && errors / Math.max(1, all.length) < THRESHOLDS.errorRate,
  serverCpuCores: samples.length > 1
    ? +((samples.at(-1).processCpuMs - samples[0].processCpuMs) / 1000 / Math.max(1, samples.at(-1).t - samples[0].t)).toFixed(2) : null,
  steps: stepResults, endpoints, timeline, server: samples,
};
fs.mkdirSync(OUT, { recursive: true });
const file = path.join(OUT, `${PROFILE}-${new Date().toISOString().replace(/[:.]/g, '-')}.json`);
fs.writeFileSync(file, JSON.stringify(summary, null, 1));
console.log(`\n${PROFILE}: ${summary.requests} requests in ${summary.durationSeconds}s (${summary.rps}/s), peak ${summary.maxUsers} users`);
console.log(`server CPU ${summary.serverCpuCores} cores on average (8 logical on this machine)`);
console.log(`overall p50 ${summary.p50} ms, p95 ${summary.p95} ms, p99 ${summary.p99} ms, errors ${(summary.errorRate * 100).toFixed(2)} % → ${summary.passed ? 'PASS' : 'FAIL'}`);
console.table(endpoints.map(({ statuses, ...e }) => e));
console.log(`saved ${file}`);
