import { readdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { join, relative, resolve } from "node:path";

const root = fileURLToPath(new URL("../apps/driver/src/", import.meta.url));
export function checkDriverStorageSource(source, path) {
  if (path.replaceAll("\\", "/") === "storage/localStore.ts") return;
  if (/\bindexedDB\b|\bDexie\b|(?:from\s*|import\s*\(|require\s*\()\s*["']dexie(?:\/|["'])/i.test(source)) {
    throw new Error(`AD-12: IndexedDB or Dexie access outside localStore.ts: ${path}`);
  }
}
export async function checkDriverStorage(directory, sourceRoot = directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      await checkDriverStorage(path, sourceRoot);
    } else if (/\.[cm]?[jt]sx?$/.test(entry.name)) {
      checkDriverStorageSource(await readFile(path, "utf8"), relative(sourceRoot, path));
    }
  }
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await checkDriverStorage(root);
  console.log("Driver storage boundary: passed.");
}
