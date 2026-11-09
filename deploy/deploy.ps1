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
    if ($health.api -eq 'ok' -and $health.database -eq 'ok') { Write-Host "Health check passed on attempt $attempt"; $healthy = $true; break }
  } catch { Write-Host "Health check attempt $attempt failed: $($_.Exception.Message)" }
  Start-Sleep -Seconds 10
}
if (-not $healthy) { throw 'Deployed API failed its health check' }

# Client routes must load directly, not only through in-app navigation.
foreach ($page in @(@{ Path = '/timesheets'; Title = 'FleetOps Office' }, @{ Path = '/driver/'; Title = 'FleetOps Driver' })) {
  $response = Invoke-WebRequest -Uri "https://fleetops-demo-zeta.vercel.app$($page.Path)" -TimeoutSec 40
  if ($response.StatusCode -ne 200 -or $response.Content -notmatch $page.Title) { throw "Deep link $($page.Path) failed" }
  Write-Host "Deep link $($page.Path) passed"
}

# Evidence only: list the Vercel Schedules so the CI log shows the nightly schedule exists. Never fails the deploy.
try {
  npx --yes vercel@65.8.0 schedules ls --token=$env:VERCEL_TOKEN --scope=$env:VERCEL_SCOPE
  if ($LASTEXITCODE) { Write-Host "Listing schedules returned exit code $LASTEXITCODE" }
} catch { Write-Host "Listing schedules failed: $($_.Exception.Message)" }
$global:LASTEXITCODE = 0
