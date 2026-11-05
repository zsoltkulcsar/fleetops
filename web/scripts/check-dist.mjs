import { existsSync } from "node:fs";

for (const file of ["dist/index.html", "dist/driver/index.html"]) {
  if (!existsSync(file)) {
    console.error(`Missing build output: ${file}`);
    process.exitCode = 1;
  }
}
