# Module boundaries

Each folder is one module of the modular monolith. Stories add `Contracts`, `Application` and `Domain` beneath the
owning module: feature entities, commands, queries and handlers belong here. Other modules use a module only through
its `Contracts`; the architecture tests enforce the boundaries. Persistence lives in `src/FleetOps.Migrations`, HTTP
endpoints in `src/FleetOps.Api`.
