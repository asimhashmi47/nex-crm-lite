// Pure helpers with no Angular imports, so they run under node --test.

/**
 * Bell alerts are live conditions, not messages. "Seen" means seen with these exact numbers:
 * once the bell is opened the badge clears, and an alert only counts again when its text changes.
 */
export interface Seenable {
  key: string;
  text: string;
}

export type SeenMap = Record<string, string>;

interface KeyValueStore {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

/** Alerts not yet seen, or whose numbers changed since they were seen. */
export function unseen<T extends Seenable>(alerts: T[], seen: SeenMap): T[] {
  return alerts.filter((a) => seen[a.key] !== a.text);
}

/** Marks what is on screen now as seen. Alerts that went away are dropped, so they count again if they return. */
export function markSeen(alerts: Seenable[]): SeenMap {
  return Object.fromEntries(alerts.map((a) => [a.key, a.text]));
}

const storageKey = (username: string) => `nx:alerts-seen:${username}`;

const nowhere: KeyValueStore = { getItem: () => null, setItem: () => {} };

/** localStorage, or a stand-in when the browser blocks it (reading the property itself can throw). */
export function browserStorage(): KeyValueStore {
  try {
    return globalThis.localStorage ?? nowhere;
  } catch {
    return nowhere;
  }
}

export function readSeen(storage: KeyValueStore, username: string): SeenMap {
  try {
    const parsed = JSON.parse(storage.getItem(storageKey(username)) ?? '{}');
    return parsed && typeof parsed === 'object' ? (parsed as SeenMap) : {};
  } catch {
    return {};
  }
}

export function writeSeen(storage: KeyValueStore, username: string, seen: SeenMap): void {
  try {
    storage.setItem(storageKey(username), JSON.stringify(seen));
  } catch {
    /* private mode or full storage: the badge just resets on the next reload */
  }
}
