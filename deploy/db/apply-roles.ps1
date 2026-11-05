$ErrorActionPreference = 'Stop'
foreach ($name in 'FLEETOPS_DB_ADMIN_URL','FLEETOPS_MIGRATION_ROLE_PASSWORD','FLEETOPS_RUNTIME_ROLE_PASSWORD','FLEETOPS_PURGE_ROLE_PASSWORD') { if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing required environment variable: $name" } }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("fleetops-roles-" + [Guid]::NewGuid() + '.sql')
try {
  $migrationPassword = $env:FLEETOPS_MIGRATION_ROLE_PASSWORD.Replace("'", "''")
  $runtimePassword = $env:FLEETOPS_RUNTIME_ROLE_PASSWORD.Replace("'", "''")
  $purgePassword = $env:FLEETOPS_PURGE_ROLE_PASSWORD.Replace("'", "''")
  @"
DO `$`$ BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'fleetops_migrator') THEN CREATE ROLE fleetops_migrator LOGIN PASSWORD '$migrationPassword'; ELSE ALTER ROLE fleetops_migrator PASSWORD '$migrationPassword'; END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'fleetops_runtime') THEN CREATE ROLE fleetops_runtime LOGIN PASSWORD '$runtimePassword'; ELSE ALTER ROLE fleetops_runtime PASSWORD '$runtimePassword'; END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'fleetops_purge') THEN CREATE ROLE fleetops_purge LOGIN PASSWORD '$purgePassword'; ELSE ALTER ROLE fleetops_purge PASSWORD '$purgePassword'; END IF;
END `$`$;
CREATE SCHEMA IF NOT EXISTS fleetops AUTHORIZATION fleetops_migrator;
GRANT USAGE ON SCHEMA fleetops TO fleetops_runtime;
REVOKE CREATE ON SCHEMA fleetops FROM fleetops_runtime;
GRANT USAGE ON SCHEMA fleetops TO fleetops_purge;
"@ | Set-Content -LiteralPath $tmp -NoNewline
  & psql $env:FLEETOPS_DB_ADMIN_URL --set=ON_ERROR_STOP=1 --file=$tmp
  if ($LASTEXITCODE) { throw "psql failed with exit code $LASTEXITCODE" }
} finally { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
