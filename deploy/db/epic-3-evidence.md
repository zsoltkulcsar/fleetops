# Epic 3 infrastructure evidence

Recorded 2026-11-09. Local verification only; no commit, push or deployment.

## Schema and routing

Story 3.2 introduced EF-generated migration `20261109101151_AddCycleGeneration`; see [its evidence](story-3-2-evidence.md). The remaining Epic 3 features use existing persisted entities. After the completed backend product Release build, `migrations has-pending-model-changes --configuration Release --no-build` exited zero with `No changes have been made to the model since the last migration.` No extra migration is required for Stories 3.3–3.6. The cached EF 10.0.4 tool warned that it is older than runtime 10.0.14; the check succeeded.

Existing `/api/(.*)` and `/jobs/(.*)` Vercel rewrites cover Epic 3 API and rollover endpoints. The nightly job runs through the existing `/jobs/nightly` schedule. No route or hosting changes were required. The route parser check could not execute locally because cached dependencies do not include `@vercel/routing-utils`; it returned `ERR_MODULE_NOT_FOUND`. CI installs pinned version 6.6.1 before this check.

## Local verification environment

The isolated PostgreSQL 18.4 server uses loopback `127.0.0.1:55433`, 40 maximum connections and 32 MB shared buffers. Its ignored data/log directory is `obj/story32-postgres`. It was restarted for Epic 3 checks and remains running until backend and browser verification complete. The review database is `fleetops_epic3_review`, with the `fleetops` schema owned by the migration role. After the backend product Release build, `database update --configuration Release --no-build` applied all 13 migrations and returned `Done.`. The fake local demo seed returned `Demo seed completed.`. A separate runtime-role connection read 28 employees, 35 vehicles and 274 documents.

For a real local API browser walk, use the existing Demo environment options: `ASPNETCORE_ENVIRONMENT=Demo`, `Auth__RequireSecondFactor=false`, `Enrolment__TestInbox=true`, and `Auth__PublicOrigin` matching the local same-origin browser proxy. The second-factor option is already restricted by code to Demo; production defaults were not changed. No real storage credentials or real personal data are used. Real storage browser uploads require a configured store; backend tests supply their existing fake object store.

Separate runtime-role SQL checks returned: schema `CREATE` privilege `false`; cycles `SELECT,INSERT,UPDATE,DELETE` privileges `true`; five installed cycle check constraints; one unique employee/pattern-slot index. Thus the generated schema and ordinary runtime access were verified against the real scratch database, including refusal of schema creation rights.

Cached web dependencies were copied to this worktree's `web/node_modules`, excluding old workspace junctions and Vite caches. All four `@fleetops` workspace junctions (`api-client`, `ui`, `office`, `driver`) now point into this worktree; build and Vite cache writes stay in the writable workspace. No dependency download was needed for this setup.

## Final verification and cleanup

Migration/schema and seeded runtime-role checks passed as described above. Executed integration/browser results and scratch server shutdown remain pending.

After the final backend product/test build, the `--no-build` pending-model check passed again. A second `database update --configuration Release --no-build` verified migration idempotency against the review database: `No migrations were applied. The database is already up to date.` and `Done.` No review data was reseeded during this check.
