// Renders the README artwork in scripts/readme-art/art.html to images/readme/*.png with the local Chrome (no extra packages).
// Run after scripts/screenshots.mjs, since the hero is built from those screenshots: node scripts/readme-art.mjs
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const OUT = path.resolve('images/readme');
const PAGE = pathToFileURL(path.resolve('scripts/readme-art/art.html')).href;
const CHROME = process.env.CHROME ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9336;
const PIECES = ['banner-light', 'banner-dark', 'hero', 'palette'];
fs.mkdirSync(OUT, { recursive: true });

const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'nx-art-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, '--no-first-run', '--hide-scrollbars', '--allow-file-access-from-files', 'about:blank'], { stdio: 'ignore' });
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

await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 1700, height: 1100, deviceScaleFactor: 2, mobile: false });
await cdp('Emulation.setDefaultBackgroundColorOverride', { color: { r: 0, g: 0, b: 0, a: 0 } });

for (const piece of PIECES) {
  await cdp('Page.navigate', { url: `${PAGE}#${piece}` });
  await sleep(600);
  await js(`document.fonts.ready.then(() => Promise.all([...document.images].map(i => i.decode().catch(() => {}))))`);
  const r = await js(`(() => { const b = document.getElementById('${piece}').firstElementChild.getBoundingClientRect(); return { x: b.x, y: b.y, width: b.width, height: b.height }; })()`);
  const { data } = await cdp('Page.captureScreenshot', { format: 'png', clip: { ...r, scale: 1 } });
  fs.writeFileSync(path.join(OUT, `${piece}.png`), Buffer.from(data, 'base64'));
  console.log(`  images/readme/${piece}.png`);
}

ws.close();
chrome.kill();
