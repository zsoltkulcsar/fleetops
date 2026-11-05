$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:FLEETOPS_DATABASE_MIGRATION_URL)) { throw 'Missing FLEETOPS_DATABASE_MIGRATION_URL' }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'deploy/db/supabase-ca.crt'))) { throw 'deploy/db/supabase-ca.crt is required' }
# dotnet ef runs from the project folder, so the CA path must be absolute.
$caPath = (Join-Path $repoRoot 'deploy/db/supabase-ca.crt')
$env:FLEETOPS_DATABASE_MIGRATION_URL = $env:FLEETOPS_DATABASE_MIGRATION_URL.Replace('deploy/db/supabase-ca.crt', $caPath)
Push-Location $repoRoot
try {
  dotnet ef database update --project 'src/FleetOps.Migrations/FleetOps.Migrations.csproj' --startup-project 'src/FleetOps.Migrations/FleetOps.Migrations.csproj'
} finally { Pop-Location }
if ($LASTEXITCODE) { throw "Migration failed with exit code $LASTEXITCODE" }
