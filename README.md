# FleetOps

FleetOps is a modular ASP.NET Core API and two-client TypeScript workspace. This story supplies the solution spine, health endpoint, migration boundary, CI and hosting configuration; product features are deliberately left to later stories.

## Local verification

```powershell
dotnet build FleetOps.slnx
dotnet test FleetOps.slnx --configuration Release
cd web
npm ci
npm run build
```

Postgres integration tests use Testcontainers and require Docker. They are expected to run in GitHub Actions even when Docker is unavailable locally.

## Required environment variables and GitHub Actions secrets

Never commit values. The following names are required by CI, deployment, migration, or role setup:

- `FLEETOPS_DB_ADMIN_URL` — privileged TLS/session-pooler URL used only by role setup.
- `FLEETOPS_DATABASE_MIGRATION_URL` — Npgsql keyword-form migration-role URL, with `SSL Mode=VerifyFull;Root Certificate=deploy/db/supabase-ca.crt` in hosted environments.
- `FLEETOPS_DATABASE_RUNTIME_URL` — Npgsql keyword-form runtime-role URL with the same TLS verification settings.
- `FLEETOPS_MIGRATION_ROLE_PASSWORD`, `FLEETOPS_RUNTIME_ROLE_PASSWORD`, `FLEETOPS_PURGE_ROLE_PASSWORD` — generated role passwords; never print them.
- `FLEETOPS_JOB_SECRET` — per-environment job authorization secret.
- `VERCEL_TOKEN`, `VERCEL_SCOPE` — CI-only Vercel deployment credentials and scope.

`deploy/db/supabase-ca.crt` is Supabase's public root CA, used for certificate verification. Local Postgres uses an ordinary local connection string; hosted connections must use session pooling on port 5432 and certificate verification.

The Vercel project is expected to be `fleetops-demo` in `fra1`; deploys happen only from CI on `main`. The Supabase Data API must remain off.
