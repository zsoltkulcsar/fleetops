import { readdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { join, relative } from "node:path";

const root = fileURLToPath(new URL("../apps/driver/src/", import.meta.url));
async function check(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      await check(path);
    } else if (relative(root, path).replaceAll("\\", "/") !== "storage/localStore.ts") {
      if ((await readFile(path, "utf8")).includes("indexedDB")) {
        throw new Error(`AD-12: IndexedDB access outside localStore.ts: ${relative(root, path)}`);
      }
    }
  }
}
await check(root);
console.log("Driver storage boundary: passed.");
