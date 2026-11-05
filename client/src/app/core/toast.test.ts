import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TOAST_MS, toastOptions } from './toast.ts';

test('toasts sit top right with a 10 second progress bar', () => {
  const o = toastOptions('success', 'Saved');
  assert.equal(o.toast, true);
  assert.equal(o.position, 'top-end');
  assert.equal(o.timer, TOAST_MS);
  assert.equal(TOAST_MS, 10_000);
  assert.equal(o.timerProgressBar, true);
});

test('toast text is passed as plain text, never HTML', () => {
  const o = toastOptions('error', '<img src=x onerror=alert(1)>', 'Server error') as Record<
    string,
    unknown
  >;
  assert.equal(o['titleText'], 'Server error');
  assert.equal(o['text'], '<img src=x onerror=alert(1)>');
  assert.equal(o['title'], undefined);
  assert.equal(o['html'], undefined);
});

test('a toast without a title uses the message as its title', () => {
  const o = toastOptions('info', 'Heads up');
  assert.equal(o.titleText, 'Heads up');
  assert.equal(o.text, undefined);
});
