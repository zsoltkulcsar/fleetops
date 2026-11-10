import { spawn } from 'node:child_process';
import { writeFile } from 'node:fs/promises';
const child = spawn('dotnet', ['bin/Release/net10.0/FleetOps.Api.dll', '--urls', 'http://127.0.0.1:5199'], { cwd: new URL('../src/FleetOps.Api/', import.meta.url), windowsHide: true, stdio: 'ignore', env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' } });
try {
  let response;
  for (let attempt = 0; attempt < 40; attempt++) {
    try { response = await fetch('http://127.0.0.1:5199/openapi/v1.json'); if (response.ok) break; } catch {}
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  if (!response?.ok) throw new Error('Release API did not expose OpenAPI');
  const document = await response.json();
  if (!document.paths['/api/v1/driver/snapshot']?.get) throw new Error('Snapshot endpoint missing');
  await writeFile(new URL('packages/api-client/openapi.json', import.meta.url), JSON.stringify(document) + '\n');
  console.log('Release Epic 3 OpenAPI exported.');
} finally { child.kill(); await new Promise(resolve => child.once('exit', resolve)); }
