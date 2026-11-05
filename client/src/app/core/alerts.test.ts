import { test } from 'node:test';
import assert from 'node:assert/strict';
import { browserStorage, markSeen, readSeen, unseen, writeSeen } from './alerts.ts';

const overdue = { key: 'overdue', text: '607 tasks are overdue' };
const today = { key: 'today', text: '78 tasks due today' };

test('every alert is unseen until the bell is opened', () => {
  assert.equal(unseen([overdue, today], {}).length, 2);
});

test('opening the bell clears the badge', () => {
  const seen = markSeen([overdue, today]);
  assert.equal(unseen([overdue, today], seen).length, 0);
});

test('an alert counts again only when its numbers change', () => {
  const seen = markSeen([overdue, today]);
  const worse = { key: 'overdue', text: '608 tasks are overdue' };
  assert.deepEqual(unseen([worse, today], seen), [worse]);
});

test('alerts that went away are forgotten, so they count again if they come back', () => {
  const seen = markSeen([overdue]);
  assert.equal(seen['today'], undefined);
  assert.equal(unseen([overdue, today], seen).length, 1);
});

test('what was seen survives a reload, per user', () => {
  const store = new Map<string, string>();
  const storage = {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, v),
  };

  writeSeen(storage, 'sara', markSeen([overdue, today]));

  assert.equal(unseen([overdue, today], readSeen(storage, 'sara')).length, 0);
  assert.equal(
    unseen([overdue, today], readSeen(storage, 'bilal')).length,
    2,
    'another user has not seen them',
  );
});

test('broken or blocked storage never breaks the bell', () => {
  const broken = {
    getItem: () => '{not json',
    setItem: () => {
      throw new Error('quota');
    },
  };
  assert.deepEqual(readSeen(broken, 'sara'), {});
  assert.doesNotThrow(() => writeSeen(broken, 'sara', { overdue: 'x' }));
});

test('without browser storage the bell falls back to a no-op store', () => {
  const store = browserStorage();
  assert.doesNotThrow(() => store.setItem('k', 'v'));
  assert.equal(typeof store.getItem('k') === 'string' || store.getItem('k') === null, true);
});
