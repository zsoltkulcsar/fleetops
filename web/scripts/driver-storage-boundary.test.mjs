import test from "node:test";
import assert from "node:assert/strict";
import { checkDriverStorageSource, checkDriverStorage } from "./check-driver-storage.mjs";

test("AD-12 rejects direct IndexedDB access in a component", () => {
  assert.throws(() => checkDriverStorageSource("window.indexedDB.open('other')", "Schedule.tsx"), /AD-12/);
});
test("AD-12 rejects Dexie imports under an alias and subpath imports", () => {
  for (const source of [
    "import Store from 'dexie'",
    "import { Table } from 'dexie/addons'",
    "const Store = require('dexie')",
    "await import('dexie')",
    "new Dexie('other')",
  ]) {
    assert.throws(() => checkDriverStorageSource(source, "storage/snapshot.ts"), /AD-12/);
  }
});
test("AD-12 permits the adapter and calls through it", () => {
  assert.doesNotThrow(() =>
    checkDriverStorageSource(
      "indexedDB.open('fleetops-driver'); new Dexie('local')",
      "storage/localStore.ts",
    ),
  );
  assert.doesNotThrow(() =>
    checkDriverStorageSource(
      "import { localStore } from './storage/localStore'; await localStore.get('snapshot');",
      "Schedule.tsx",
    ),
  );
});
test("AD-12 scans the current driver source tree", async () => {
  await assert.doesNotReject(
    checkDriverStorage(
      new URL("../apps/driver/src", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"),
    ),
  );
});
