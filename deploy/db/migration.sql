-- Idempotent grants applied after EF migrations.
GRANT USAGE ON SCHEMA fleetops TO fleetops_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA fleetops TO fleetops_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE fleetops_migrator IN SCHEMA fleetops GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fleetops_runtime;
REVOKE CREATE ON SCHEMA fleetops FROM fleetops_runtime;
