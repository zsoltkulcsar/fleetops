// Vercel refuses a deploy whose rewrite patterns it cannot parse, after CI has passed.
// Validate every rewrite and header source here with Vercel's own parser so CI fails first.
import { readFileSync } from "node:fs";
import { getTransformedRoutes } from "@vercel/routing-utils";

const config = JSON.parse(readFileSync(new URL("../../vercel.json", import.meta.url), "utf8"));
const groups = [["root", config], ...Object.entries(config.services ?? {})];
let failed = false;
for (const [name, group] of groups) {
  const result = getTransformedRoutes({ rewrites: group.rewrites, headers: group.headers });
  if (result.error) {
    console.error(`vercel.json ${name}: ${result.error.message}`);
    failed = true;
  }
}
if (failed) process.exit(1);
console.log("Vercel routes: valid.");
