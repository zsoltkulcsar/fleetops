import { spawn } from "node:child_process";
import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const apiProject = fileURLToPath(new URL("../../src/FleetOps.Api/FleetOps.Api.csproj", import.meta.url));
const port = 5197;
const child = spawn(
  "dotnet",
  [
    "run",
    "--no-launch-profile",
    "--project",
    apiProject,
    "--no-restore",
    "--urls",
    `http://127.0.0.1:${port}`,
  ],
  { stdio: "ignore", windowsHide: true },
);
const stop = () => child.kill();
process.on("exit", stop);
try {
  let response;
  for (let attempt = 0; attempt < 60; attempt++) {
    try {
      response = await fetch(`http://127.0.0.1:${port}/openapi/v1.json`);
      if (response.ok) break;
    } catch {}
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  if (!response?.ok) throw new Error("API did not expose OpenAPI before timeout");
  const document = JSON.stringify(await response.json()) + "\n";
  const path = new URL("../packages/api-client/openapi.json", import.meta.url);
  const checkedIn = await readFile(path, "utf8");
  await writeFile(path, document);
  if (checkedIn !== document) process.exitCode = 1;
} finally {
  stop();
}
