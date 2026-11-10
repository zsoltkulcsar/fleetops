# Epic 4 progress — 09.11.2026

Status: started; no Epic 4 story is complete yet.

Implemented groundwork for Story 4.1:

- Typed command envelopes with command ID, schema version, aggregate, base version, device time and payload.
- Encrypted LocalStore queue with a cross-tab Web Lock around read-modify-write operations, acknowledgement by applied IDs, and retained failure/conflict state.
- No age-based eviction; errors writing storage propagate to the caller.
- Queue removal alongside the snapshot on vault replacement, lockout and revocation/sign-out.
- Reusable JPEG compression with resize retries capped at 1,500,000 bytes; existing document uploads use it, including uncropped photos.
- Reusable base64 photo encoding and persistence/quota preflight.
- Driver shell status for queued items, seven days without a successful session check, and low storage. It uses existing driver classes and renders nothing when there is no status to show.
- Eleven new Node unit tests plus four existing storage boundary tests, included in CI.

Important remaining work:

- No daily action calls the new queue yet. Document uploads still require a connection. Photo encoding and storage preflight are available but are not yet wired into an offline upload action.
- Story 4.1 still needs offline photo enqueue and upload replay (request a fresh link, upload, confirm, erase the local photo after acknowledgement), with safe recovery from a lost confirmation response.
- Story 4.2 needs server-side command receipts/idempotency, bounded batch sync, snapshot applied IDs, schema-version handlers and startup/focus/network flush.
- Stories 4.3–4.8 remain unimplemented: start/readiness, activities, end/nights/Cycle completion, conflicts, and weekly submission/reminders.
- NEEDS_MIGRATION: server command receipt and conflict records plus Shift, Activity and Overnight persistence and a partial unique index for one running Shift per employee. No backend model or migration changes have been made in this pass. AGENTS.md reserves migration generation to the orchestrator.

Validation:

```text
Test integrity passed: 516 C# tests, 25 Playwright files.
Build succeeded.
    18 Warning(s)
    0 Error(s)
```

`npm run build` exited 0, including office/driver typechecking, generated client typechecking, E2E typechecking, production builds and the dist check. Initial sandbox Vite build failed at `spawn EPERM`; the approved unsandboxed build passed.

```text
node --test --test-isolation=none scripts/driver-command-queue.test.mjs scripts/driver-storage-boundary.test.mjs
ℹ tests 15
ℹ pass 15
ℹ fail 0
```

Tests exercise the queue implementation with an in-memory durable-storage port and the photo codec with a canvas port; they are not browser/IndexedDB end-to-end tests. No Playwright or live Demo checks ran. Global `npm run format:check` reports formatting issues in 97 unchanged files; a targeted Prettier check passes for all changed frontend files. Nothing was committed, pushed or deployed.
