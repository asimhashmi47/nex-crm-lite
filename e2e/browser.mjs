// A thin Chrome DevTools Protocol driver for the E2E journeys (same approach as scripts/screenshots.mjs, no extra packages).
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const CHROME = process.env.CHROME ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';

export async function launch({ app = process.env.APP ?? 'http://localhost:4300', port = 9340 } = {}) {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'nx-e2e-'));
  const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });

  let target;
  for (let i = 0; i < 80 && !target; i++) {
    await new Promise((r) => setTimeout(r, 125));
    try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find((t) => t.type === 'page'); } catch { /* starting */ }
  }
  if (!target) throw new Error('Chrome did not start');

  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener('open', r, { once: true }));
  let seq = 0;
  const pending = new Map();
  ws.addEventListener('message', (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  });
  const cdp = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++seq;
    pending.set(id, (m) => (m.error ? reject(new Error(`${method}: ${m.error.message}`)) : resolve(m.result)));
    ws.send(JSON.stringify({ id, method, params }));
  });

  await cdp('Page.enable');
  await cdp('Runtime.enable');
  await cdp('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });

  const page = {
    async js(expression) {
      const r = await cdp('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (r.exceptionDetails) throw new Error(`Page error: ${r.exceptionDetails.exception?.description ?? r.exceptionDetails.text}`);
      return r.result?.value;
    },
    /** Polls a page expression until it is truthy; fails with the description after the timeout. */
    async waitFor(expression, description = expression, timeout = 10_000) {
      const until = Date.now() + timeout;
      while (Date.now() < until) {
        const v = await page.js(`(() => { try { return ${expression}; } catch { return false; } })()`);
        if (v) return v;
        await new Promise((r) => setTimeout(r, 100));
      }
      throw new Error(`Timed out waiting for: ${description}`);
    },
    async go(url) {
      await cdp('Page.navigate', { url: app + url });
      await page.waitFor(`document.readyState === 'complete' && !!document.querySelector('app-root')?.children.length`, `page ${url}`);
    },
    path: () => page.js('location.pathname + location.search'),
    /** Clicks the first visible element under `selector` whose text includes `text`. */
    async click(text, selector = 'button, a, [role=tab]') {
      const ok = await page.waitFor(
        `(() => { const el = [...document.querySelectorAll(${JSON.stringify(selector)})].find(e => e.offsetParent !== null && e.textContent.trim().includes(${JSON.stringify(text)})); if (el) { el.click(); return true; } return false; })()`,
        `clickable "${text}"`);
      return ok;
    },
    /** Types into an input the way a person does: focus, select all, insert text. */
    async type(selector, text) {
      await page.waitFor(`!!document.querySelector(${JSON.stringify(selector)})`, `input ${selector}`);
      await page.js(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); el.focus(); el.select?.(); })()`);
      await cdp('Input.insertText', { text });
    },
    toastText: () => page.js(`[...document.querySelectorAll('.swal2-toast')].map(t => t.textContent.trim()).join(' | ')`),
    clearToasts: () => page.js(`document.querySelectorAll('.swal2-container').forEach(e => e.remove())`),
    async signOut() {
      // Storage belongs to the app's origin, so load the app before clearing it.
      await page.go('/login');
      await page.js('sessionStorage.clear()');
      await page.go('/login');
      await page.waitFor(`location.pathname === '/login'`, 'signed out');
    },
    async signInAs(username) {
      await page.signOut();
      await page.click(username, 'button');
      await page.waitFor(`location.pathname !== '/login'`, `signed in as ${username}`);
      await page.clearToasts();
    },
  };

  return {
    page,
    async close() {
      ws.close();
      chrome.kill();
    },
  };
}
