// Captures the README screenshots with the locally installed Chrome over the DevTools protocol (no extra packages).
// Needs the API (5090) and the Angular dev server (4300) running with demo data.
// Run: node scripts/screenshots.mjs
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const APP = process.env.APP ?? 'http://localhost:4300';
const OUT = path.resolve('images');
const CHROME = process.env.CHROME ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9335;
fs.mkdirSync(OUT, { recursive: true });

const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'nx-shots-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, '--no-first-run', '--hide-scrollbars', '--force-device-scale-factor=1', 'about:blank'], { stdio: 'ignore' });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let target;
for (let i = 0; i < 40 && !target; i++) {
  await sleep(250);
  try { target = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch { /* starting */ }
}
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => ws.addEventListener('open', r, { once: true }));
let seq = 0;
const pending = new Map();
ws.addEventListener('message', (e) => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } });
const cdp = (method, params = {}) => new Promise((resolve, reject) => {
  const id = ++seq;
  pending.set(id, (m) => (m.error ? reject(new Error(`${method}: ${m.error.message}`)) : resolve(m.result)));
  ws.send(JSON.stringify({ id, method, params }));
});
const js = async (expression) => (await cdp('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true })).result?.value;

const size = (width, height, mobile = false) => cdp('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile });
async function go(url, wait = 3000) { await cdp('Page.navigate', { url: APP + url }); await sleep(wait); }
async function shot(name) {
  const { data } = await cdp('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(path.join(OUT, `${name}.png`), Buffer.from(data, 'base64'));
  console.log(`  images/${name}.png`);
}
const click = (text, selector = 'button, a, [role=tab]') =>
  js(`(() => { const el = [...document.querySelectorAll(${JSON.stringify(selector)})].find(e => e.textContent.trim().includes(${JSON.stringify(text)})); if (el) { el.click(); return true; } return false; })()`);
const clickLabel = (label) =>
  js(`(() => { const el = document.querySelector('[aria-label^=${JSON.stringify(label)}]'); el?.click(); return !!el; })()`);
// Toasts from the previous step would sit on top of the next screenshot.
const clearToasts = () => js(`document.querySelectorAll('.swal2-container').forEach(e => e.remove())`);

await cdp('Page.enable');
await cdp('Runtime.enable');
await size(1440, 900);

console.log('Capturing…');
await go('/login', 2500);
await shot('01-login');
await click('admin', 'button');
await sleep(4000);
await clearToasts();

await go('/dashboard', 4500);
await shot('02-dashboard');
await js('window.scrollTo(0, document.body.scrollHeight)');
await sleep(1200);
await shot('02b-dashboard-lower');

await go('/deals', 3500);
await shot('03-pipeline-board');
await js(`document.querySelector('article[draggable]')?.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))`);
await sleep(2000);
await shot('04-deal-editor');

await go('/deals?view=list', 3500);
await shot('05-deals-list');
await click('Advanced filter');
await sleep(400);
await js(`(() => { const s = document.querySelectorAll('select[aria-label="Rule 1 field"]')[0]; if (s) { s.value = 'amount'; s.dispatchEvent(new Event('change')); } })()`);
await sleep(300);
await js(`(() => { const op = document.querySelector('select[aria-label="Operator"]'); if (op) { op.value = 'gt'; op.dispatchEvent(new Event('change')); } const v = document.querySelector('input[aria-label="Value"]'); if (v) { v.value = '50000'; v.dispatchEvent(new Event('input')); } })()`);
await sleep(300);
await click('Apply filter');
await sleep(1800);
await click('Advanced filter');
await sleep(600);
await shot('05b-advanced-filter');

await go('/leads', 3500);
await shot('06-leads');
await go('/leads?status=Qualified', 3500);
await click('Convert', 'td button');
await sleep(1200);
await shot('06b-lead-convert');
await js(`document.querySelector('.swal2-cancel')?.click()`);
await sleep(600);

await go('/contacts', 3500);
await shot('07-contacts');
await click('New contact');
await sleep(800);
await click('Add contact');
await sleep(800);
await shot('07b-contact-validation');

await go('/companies', 3500);
await shot('08-companies');
await click('Download', 'button');
await sleep(600);
await shot('08b-export-menu');

await go('/tasks', 3500);
await shot('09-tasks');
await js(`document.querySelector('tbody button[aria-label^="Complete"]')?.click()`);
await sleep(1500);
await shot('09b-task-done-toast');
await go('/tasks?due=overdue', 3500);
await shot('09c-tasks-overdue');

await go('/tasks', 3000);
await click('New task');
await sleep(1200);
await shot('10-task-editor');

await go('/dashboard', 3500);
await js(`document.querySelector('[data-pop="bell"] button')?.click()`);
await sleep(800);
await shot('11-notifications');

await size(390, 844, true);
await go('/dashboard', 4500);
await shot('12-mobile-dashboard');
await go('/deals', 3500);
await shot('13-mobile-pipeline');

ws.close();
chrome.kill();
console.log('Done.');
