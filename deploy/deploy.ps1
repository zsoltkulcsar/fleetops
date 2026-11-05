$ErrorActionPreference = 'Stop'
foreach ($name in 'VERCEL_TOKEN','VERCEL_SCOPE') { if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing required environment variable: $name" } }
npx --yes vercel@65.8.0 link --yes --project=fleetops-demo --token=$env:VERCEL_TOKEN --scope=$env:VERCEL_SCOPE
if ($LASTEXITCODE) { throw "Vercel project linking failed with exit code $LASTEXITCODE" }
npx --yes vercel@65.8.0 deploy --prod --token=$env:VERCEL_TOKEN --scope=$env:VERCEL_SCOPE --yes
if ($LASTEXITCODE) { throw "Vercel deployment failed with exit code $LASTEXITCODE" }

# A finished deploy does not prove the API runs, so check it from outside.
$healthUrl = 'https://fleetops-demo-zeta.vercel.app/healthz'
foreach ($attempt in 1..6) {
  try {
    $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 40
    if ($health.api -eq 'ok' -and $health.database -eq 'ok') { Write-Host "Health check passed on attempt $attempt"; exit 0 }
  } catch { Write-Host "Health check attempt $attempt failed: $($_.Exception.Message)" }
  Start-Sleep -Seconds 10
}
throw 'Deployed API failed its health check'
