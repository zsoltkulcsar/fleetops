# FleetOps: rules for coding agents

You write product code and tests in this repository. These rules hold for every task, on top of your task contract. If a rule and a contract disagree, stop and say so.

## What done means

Done means you ran it and it passed. Before you finish, in this order:

1. `node scripts/check-test-integrity.mjs`
2. Build with the command in "Build limits".
3. Run the tests for what you changed (see "Running tests").
4. Report the exact summary lines from your last run.

If you cannot finish something, say so by name. An honest "not done" is a good result. These are failures, and each one costs more than a missing test:

- Reporting work as done that the code does not show.
- Saying "implemented in code" when a test was asked for.
- Blaming the sandbox without quoting the line that proves it.

## Tests must be able to fail

When a contract lists test names, those tests do not exist yet. Listing them means: create them now. "The tests are absent" is never a result, because writing them is the task.

A test is real only if breaking the behaviour it names makes it fail. People will break the code on purpose to check. So:

- Never write `Assert.True(true)`, `expect(true)`, an empty body, or a test with no assertion.
- Never make a test call another test. `Foo() => await Bar();` proves nothing about Foo. Shared setup goes in helpers that are not tests.
- Drive the real thing. API tests go through HTTP against real Postgres. Assert the status, the `code`, and the row in the database.
- Assert what the name says. `Concurrent_...` must run requests in parallel. `..._rolls_back_...` must fail after the write and check from a second connection. `..._is_stored_only_as_a_hash` must read the row.
- A test must not be able to pass for a different reason. If two rules could cause a refusal, set the test up so that only the one it names can.
- Do not delete, rename, skip or weaken a test to get green. If a test is wrong, say why in your result.
- If a test fails because the product is wrong, fix the product.

## Security

- Fail closed. Missing session, role, setting, row or key refuses. Never `?? ""` on a secret.
- Use the framework's security features. Do not write your own crypto, lockout, or token handling when ASP.NET Core Identity has it.
- Never log passwords, tokens, codes, cookies, e-mails or request values. Logging is allow-listed.
- Validate input and return 400 with field names, never values. Never let bad input become a 500.
- Refusals never reveal whether an account exists.

## Architecture

- Follow `src/Modules/README.md`. Modules depend downward only.
- Every state change is one command through `CommandDispatcher`. Every read is a query through `QueryDispatcher`. No endpoint touches data directly.
- Business logic lives in a module's `Application`, never in `FleetOps.Migrations` or an endpoint.
- Reads return read models with `[VisibleTo]`, never entities.
- Do not edit specs, domain or design files. They live outside this repository. If one must change, write `CONTRACT_CHANGE_REQUEST: <what and why>` in your result.

## Code style

- One statement per line. Descriptive names, not `a`, `r`, `s`. One concept per file.
- Web code passes `npm run format:check`.
- No debug output left behind.
- Never `.GetAwaiter().GetResult()`, never `.Result` on a task.
- Never type non-ASCII characters into source files: your file writes on this machine turn them into ` - `, `?` or
  U+FFFD. In TypeScript and C# strings use escapes: `·` (·), `→` (→), `–` (–), `—` (—),
  `…` (…). Do not swap a design character for an ASCII look-alike: the UI copy follows the design text.

## Build limits

Past runs exhausted this machine's memory. Use only:

```
dotnet build <one project> -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false --no-restore
```

Never run `dotnet ef` or `dotnet-ef`: inside the sandbox it starts hundreds of MSBuild workers. If the model needs a migration, write `NEEDS_MIGRATION: <what>` in your result and the orchestrator adds it. Never `-v:diag`. Never loop on a build that fails the same way. Packages are restored for you: if you need a new one, stop and name it.

## Running tests

`dotnet test` and Docker are blocked in the sandbox, and so is Playwright. Run test executables directly against the local Postgres:

```
FLEETOPS_TEST_ADMIN_URL=Host=127.0.0.1;Port=55432;Username=postgres;Password=local-test-only
tests/<Project>/bin/Release/net10.0/<Project>.exe -method "*Name*"
```

The Testcontainers path must keep working too, because CI uses it. Change both or neither.

## Migrations

Migrations come from `dotnet ef`, run by the orchestrator, never by hand. Never edit a migration that is already on `main`.

## Git

Never commit or push. The orchestrator does that.
