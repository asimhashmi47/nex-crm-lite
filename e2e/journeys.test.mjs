// Critical user journeys, end to end: real Chrome, real Angular app, real API and SQLite.
// Needs the API (5090) and the client dev server (4300) running with demo data.
// Run: node --test e2e/
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { launch } from './browser.mjs';

let browser;
let page;
const unique = Date.now().toString(36);

before(async () => {
  browser = await launch();
  page = browser.page;
});

after(async () => {
  await browser?.close();
});

test('UI-2: signed-out users go to sign-in with a return URL, and come back after signing in', async () => {
  await page.signOut();
  await page.go('/deals');
  await page.waitFor(`location.pathname === '/login'`, 'redirect to /login');
  assert.equal(await page.path(), '/login?returnUrl=%2Fdeals');

  await page.click('admin', 'button');
  await page.waitFor(`location.pathname === '/deals'`, 'return to /deals');

  await page.go('/login');
  await page.waitFor(`location.pathname === '/dashboard'`, 'signed-in user sent to the dashboard');
});

test('UI-3: dashboard shows eight KPI tiles and its charts, and opens the pipeline board', async () => {
  await page.signInAs('admin');
  await page.go('/dashboard');
  await page.waitFor(`document.querySelectorAll('app-stat').length === 8`, '8 KPI tiles');
  await page.waitFor(`document.querySelectorAll('.apexcharts-canvas').length >= 3`, '3 charts');

  await page.click('Open pipeline', 'a');
  await page.waitFor(`document.querySelectorAll('section[aria-label$=" column"]').length === 6`, '6 board columns');
  const cards = await page.js(`document.querySelectorAll('article[draggable]').length`);
  assert.ok(cards > 0, 'the board shows deal cards');
});

test('UI-4: creating a contact shows field errors first, then saves and is searchable', async () => {
  await page.signInAs('sara');
  await page.go('/contacts');
  await page.click('New contact');
  await page.click('Add contact');
  await page.waitFor(`document.body.innerText.includes('First name is required.') && document.body.innerText.includes('Email is required.')`, 'required-field errors');

  const email = `e2e.${unique}@journey.test`;
  await page.type('#ct-first', 'Zoë');
  await page.type('#ct-last', `Journey ${unique}`);
  await page.type('#ct-email', email.toUpperCase());
  await page.click('Add contact');

  await page.waitFor(`document.querySelector('.swal2-toast')?.textContent.includes('Contact saved')`, 'saved toast');
  await page.waitFor(`!document.querySelector('[role=dialog]')`, 'drawer closed');
  await page.type('input[data-search]', unique);
  await page.waitFor(`document.querySelector('tbody')?.innerText.includes(${JSON.stringify(email)})`, 'new row, email stored lower-case');
});

test('UI-1: toasts sit top right with a progress bar and render user text as plain text', async () => {
  await page.signInAs('sara');
  await page.go('/contacts');
  await page.js('window.__xss = 0');
  await page.click('New contact');
  await page.type('#ct-first', `<img src=x onerror="window.__xss=1">`);
  await page.type('#ct-last', 'Payload');
  await page.type('#ct-email', `xss.${unique}@journey.test`);
  await page.click('Add contact');

  await page.waitFor(`!!document.querySelector('.swal2-toast')`, 'toast');
  assert.ok(await page.js(`document.querySelector('.swal2-container').classList.contains('swal2-top-end')`), 'top right');
  assert.ok(await page.js(`!!document.querySelector('.swal2-toast .swal2-timer-progress-bar')`), 'progress bar');
  assert.ok((await page.toastText()).includes('<img src=x'), 'payload shown as text');
  // SweetAlert2 ships its own hidden <img class="swal2-image">; an injected one would carry the onerror attribute.
  assert.equal(await page.js(`document.querySelectorAll('[onerror]').length`), 0, 'no injected element');
  assert.equal(await page.js('window.__xss'), 0, 'onerror never ran');
});

test('UI-5: converting a qualified lead asks first, then confirms with a toast', async () => {
  await page.signInAs('sara');
  await page.go('/leads?status=Qualified');
  await page.waitFor(`document.querySelectorAll('tbody tr td button').length > 0`, 'qualified leads');
  const before = await page.js(`+document.querySelector('.badge.tone-gray.tabular').textContent.replace(/,/g, '')`);

  await page.click('Convert', 'td button');
  await page.waitFor(`document.querySelector('.swal2-title')?.textContent === 'Convert to a deal?'`, 'confirm dialog');
  await page.click('Convert lead', '.swal2-confirm');

  await page.waitFor(`document.querySelector('.swal2-toast')?.textContent.includes('Lead converted')`, 'converted toast');
  await page.waitFor(`+document.querySelector('.badge.tone-gray.tabular').textContent.replace(/,/g, '') === ${before - 1}`, 'one fewer qualified lead');
});

test('UI-6: ticking off a task removes it from the open list', async () => {
  await page.signInAs('sara');
  await page.go('/tasks');
  await page.waitFor(`!!document.querySelector('tbody button[aria-label^="Complete"]')`, 'open tasks');
  const before = await page.js(`+document.querySelector('.badge.tone-gray.tabular').textContent.replace(/,/g, '')`);

  await page.js(`document.querySelector('tbody button[aria-label^="Complete"]').click()`);

  await page.waitFor(`document.querySelector('.swal2-toast')?.textContent.includes('Nice work')`, 'done toast');
  await page.waitFor(`+document.querySelector('.badge.tone-gray.tabular').textContent.replace(/,/g, '') === ${before - 1}`, 'one fewer open task');
});

test('UI-7: sales reps never see delete buttons; managers do', async () => {
  await page.signInAs('sara');
  await page.go('/companies');
  await page.waitFor(`document.querySelectorAll('tbody tr').length > 1`, 'company rows');
  assert.equal(await page.js(`document.querySelectorAll('button[aria-label^="Delete"]').length`), 0);

  await page.signInAs('manager');
  await page.go('/companies');
  await page.waitFor(`document.querySelectorAll('button[aria-label^="Delete"]').length > 0`, 'delete buttons for a manager');
});

test('UI-8: signing out from the menu ends the session on the server too (F-02)', async () => {
  await page.signInAs('manager');
  await page.go('/dashboard');
  const token = await page.js(`JSON.parse(sessionStorage.getItem('nx:session')).token`);

  await page.click('Manager', 'button[aria-label="Account menu"]');
  await page.click('Sign out', '[role=menuitem]');
  await page.waitFor(`location.pathname === '/login'`, 'back on sign-in');

  const status = await page.js(`fetch('/api/dashboard', { headers: { Authorization: 'Bearer ' + ${JSON.stringify(token)} } }).then(r => r.status)`);
  assert.equal(status, 401, 'the old token is dead');
});

test('UI-9: only admins see and open the audit log (F-06)', async () => {
  await page.signInAs('sara');
  await page.go('/dashboard');
  assert.equal(await page.js(`[...document.querySelectorAll('nav a')].some(a => a.textContent.includes('Audit log'))`), false);
  await page.go('/audit');
  await page.waitFor(`location.pathname === '/dashboard'`, 'rep bounced from /audit');

  await page.signInAs('admin');
  await page.click('Audit log', 'nav a');
  // The grid shows skeleton rows while loading, so wait for real audit text.
  await page.waitFor(`document.querySelector('tbody')?.innerText.includes('Auth.SignedIn')`, 'sign-ins recorded in the audit log');
  assert.equal(await page.js(`[...document.querySelectorAll('button')].some(b => b.textContent.includes('Download'))`), false, 'no export on the audit log');
});

test('UI-10: opening the bell clears its badge, and it stays cleared after clicking an alert and reloading', async () => {
  await page.signInAs('manager');
  await page.js(`Object.keys(localStorage).filter(k => k.startsWith('nx:alerts-seen:')).forEach(k => localStorage.removeItem(k))`);
  await page.go('/dashboard');
  const badge = `document.querySelector('[data-pop="bell"] button span')?.textContent.trim() ?? ''`;
  await page.waitFor(`(${badge}) !== ''`, 'an unread badge');

  await page.js(`document.querySelector('[data-pop="bell"] button').click()`);
  await page.waitFor(`(${badge}) === ''`, 'badge cleared on open');

  await page.js(`document.querySelector('[data-pop="bell"] .menu-item').click()`);
  await page.waitFor(`location.pathname !== '/dashboard'`, 'followed the alert');
  assert.equal(await page.js(badge), '', 'still cleared after clicking an alert');

  await page.go('/dashboard');
  await page.waitFor(`!!document.querySelector('[data-pop="bell"] button')`, 'shell after reload');
  await new Promise((r) => setTimeout(r, 1500));
  assert.equal(await page.js(badge), '', 'still cleared after a reload');
});
