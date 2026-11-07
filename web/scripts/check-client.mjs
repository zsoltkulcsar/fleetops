import { execFileSync, spawn } from "node:child_process";
import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const apiProject = fileURLToPath(new URL("../../src/FleetOps.Api/FleetOps.Api.csproj", import.meta.url));
const port = 5197;
const apiDirectory = fileURLToPath(new URL("../../src/FleetOps.Api/", import.meta.url));
// Build with the limits from AGENTS.md: without them MSBuild starts workers it cannot reach
// inside the Codex sandbox, and keeps starting more. Run the DLL directly so kill() stops the API.
execFileSync(
  "dotnet",
  ["build", apiProject, "-c", "Debug", "-m:1", "-nodeReuse:false", "-p:UseSharedCompilation=false", "--no-restore"],
  { stdio: "ignore", windowsHide: true, env: { ...process.env, MSBUILDDISABLENODEREUSE: "1" } },
);
const child = spawn("dotnet", ["bin/Debug/net10.0/FleetOps.Api.dll", "--urls", `http://127.0.0.1:${port}`], {
  cwd: apiDirectory,
  stdio: "ignore",
  windowsHide: true,
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: "Development" },
});
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
