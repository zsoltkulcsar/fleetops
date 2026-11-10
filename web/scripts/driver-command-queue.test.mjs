import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

async function loadSource(path) {
  const source = await readFile(new URL(path, import.meta.url), "utf8");
  const { outputText } = ts.transpileModule(source, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 },
  });
  return import(`data:text/javascript;base64,${Buffer.from(outputText).toString("base64")}`);
}
const { createCommandQueue } = await loadSource("../apps/driver/src/storage/commandQueueCore.ts");
const { replayDocument } = await loadSource("../apps/driver/src/storage/documentReplay.ts");
const { projectShifts, shiftHours, companyDate, resolveSuggestedTruck } = await loadSource(
  "../apps/driver/src/storage/shiftProjection.ts",
);
const { companyInstant } = await loadSource("../apps/driver/src/shiftTime.ts");
const { replayShifts } = await loadSource("../apps/driver/src/storage/shiftReplay.ts");
const { compressPhoto, ensurePhotoSpace, photoPayload } = await loadSource(
  "../apps/driver/src/storage/photo.ts",
);

function fixture() {
  let persisted = [];
  let writesFail = false;
  let lock = Promise.resolve();
  const storage = {
    read: async () => structuredClone(persisted),
    write: async (commands) => {
      if (writesFail) throw new Error("quota_exceeded");
      persisted = structuredClone(commands);
    },
    exclusive: (operation) => {
      const result = lock.then(operation);
      lock = result.catch(() => {});
      return result;
    },
  };
  return {
    storage,
    queue: createCommandQueue(storage),
    failWrites: () => {
      writesFail = true;
    },
  };
}
const command = (id = "one", aggregateId = "shift-one") => ({
  commandId: id,
  type: "shift.start",
  schemaVersion: 1,
  aggregateId,
  baseVersion: 0,
  deviceTime: "2026-11-09T08:00:00Z",
  payload: { truckId: "truck-one" },
  state: "waiting",
});

const documentCommand = () => ({
  ...command("document-one", "document:employee:code95"),
  type: "document.upload",
  payload: {
    type: "code95",
    number: null,
    expiresAt: "2027-10-01",
    photos: [{ page: "page", contentType: "image/jpeg", data: "AAECAw==" }],
  },
});

const shiftSnapshot = () => ({
  generatedAt: "2026-11-09T08:00:00Z",
  companyTimeZone: "Europe/Berlin",
  shifts: [],
  appliedCommandIds: [],
  trucks: [],
  suggestedTruckId: null,
});

test("shift sync drains bounded ordered batches before storing one snapshot and acknowledging", async () => {
  const { queue } = fixture();
  const commands = Array.from({ length: 45 }, (_, index) =>
    shiftCommand(String(index), "shift.switch", { type: "other_work" }),
  );
  await queue.enqueueMany(commands);
  const events = [];
  const delivered = [];
  await replayShifts({
    list: queue.list,
    checkpoint: queue.checkpoint,
    mark: queue.mark,
    send: async (batch) => {
      events.push(`batch:${batch.length}`);
      delivered.push(...batch.map((item) => item.commandId));
      assert.ok(batch.length <= 20);
      return {
        results: batch.map((item) => ({
          commandId: item.commandId,
          status: "applied",
          code: null,
          shift: null,
        })),
      };
    },
    snapshot: async () => {
      events.push("snapshot");
      return { ...shiftSnapshot(), appliedCommandIds: delivered };
    },
    replace: async () => {
      events.push("stored");
      assert.equal((await queue.list()).length, 45);
    },
    acknowledge: async (ids) => {
      events.push("acknowledged");
      await queue.acknowledge(ids);
    },
  });
  assert.deepEqual(
    delivered,
    commands.map((item) => item.commandId),
  );
  assert.deepEqual(events, ["batch:20", "batch:20", "batch:5", "snapshot", "stored", "acknowledged"]);
  assert.deepEqual(await queue.list(), []);
});

test("a snapshot storage failure leaves applied commands queued for safe replay", async () => {
  const { queue } = fixture();
  await queue.enqueue(shiftCommand("one", "shift.start", { workWithoutVehicle: true }));
  let acknowledged = false;
  await assert.rejects(
    replayShifts({
      list: queue.list,
      checkpoint: queue.checkpoint,
      mark: queue.mark,
      send: async (batch) => ({
        results: batch.map((item) => ({
          commandId: item.commandId,
          status: "applied",
          code: null,
          shift: null,
        })),
      }),
      snapshot: async () => ({ ...shiftSnapshot(), appliedCommandIds: ["one"] }),
      replace: async () => {
        throw new Error("storage_failed");
      },
      acknowledge: async () => {
        acknowledged = true;
      },
    }),
    /storage_failed/,
  );
  assert.equal(acknowledged, false);
  assert.equal((await queue.list())[0].commandId, "one");
});

test("per-item sync failure keeps its payload and permits other aggregates to finish", async () => {
  const { queue } = fixture();
  const failed = shiftCommand("one", "shift.start", { workWithoutVehicle: true });
  const other = {
    ...shiftCommand("two", "shift.start", { workWithoutVehicle: true }),
    aggregateId: "other-shift",
  };
  await queue.enqueueMany([failed, other]);
  await replayShifts({
    list: queue.list,
    checkpoint: queue.checkpoint,
    mark: queue.mark,
    send: async () => ({
      results: [
        { commandId: "one", status: "failed", code: "running_shift_exists", shift: null },
        { commandId: "two", status: "applied", code: null, shift: null },
      ],
    }),
    snapshot: async () => ({ ...shiftSnapshot(), appliedCommandIds: ["two"] }),
    replace: async () => {},
    acknowledge: queue.acknowledge,
  });
  const saved = await queue.list();
  assert.equal(saved.length, 1);
  assert.deepEqual(saved[0].payload, failed.payload);
  assert.equal(saved[0].errorCode, "running_shift_exists");
  assert.equal(saved[0].state, "failed");
});
const shiftCommand = (id, type, payload, time = "2026-11-09T08:00:00Z") => ({
  ...command(id),
  type,
  aggregateId: "shift-one",
  payload,
  deviceTime: time,
});

test("offline truck selection follows swap end dates and the saved Cycle calendar", () => {
  const snapshot = {
    ...shiftSnapshot(),
    trucks: [
      { id: "swap", kind: "truck" },
      { id: "cycle", kind: "truck" },
    ],
    assignments: [{ vehicleId: "swap", from: "2026-11-01", to: "2026-11-09" }],
  };
  const cycles = [{ vehicleId: "cycle", from: "2026-11-01", to: "2026-11-15", status: "planned" }];
  assert.equal(resolveSuggestedTruck(snapshot, cycles, "2026-11-09"), "swap");
  assert.equal(resolveSuggestedTruck(snapshot, cycles, "2026-11-10"), "cycle");
  assert.equal(resolveSuggestedTruck(snapshot, cycles, "2026-11-20"), null);
});

test("offline day projects start, activity, end, night and submission before any server response", () => {
  const commands = [
    shiftCommand("start", "shift.start", { workWithoutVehicle: true }),
    shiftCommand("switch", "shift.switch", { type: "availability" }, "2026-11-09T09:00:00Z"),
    shiftCommand("end", "shift.end", {}, "2026-11-09T16:00:00Z"),
    shiftCommand("night", "shift.night", { night: "2026-11-09", countryCode: "DE", kind: "cab" }),
    shiftCommand("submit", "shift.submit", {}),
  ];
  const shift = projectShifts(shiftSnapshot(), commands)[0];
  assert.equal(shift.status, "submitted");
  assert.equal(shift.version, 5);
  assert.equal(shiftHours(shift), 8);
  assert.equal(shift.activities[0].endAt, shift.activities[1].startAt);
  assert.equal(shift.activities[0].id, "shift-one");
  assert.equal(shift.activities[1].id, "switch");
  assert.equal(shift.overnights[0].countryCode, "DE");
});

test("snapshot applied IDs prevent double projection after a lost acknowledgement", () => {
  const start = shiftCommand("start", "shift.start", { workWithoutVehicle: true });
  const change = shiftCommand("switch", "shift.switch", { type: "break" }, "2026-11-09T09:00:00Z");
  const original = projectShifts(shiftSnapshot(), [start, change]);
  const snapshot = { ...shiftSnapshot(), shifts: original, appliedCommandIds: ["start", "switch"] };
  assert.deepEqual(projectShifts(snapshot, [start, change]), original);
  assert.equal(snapshot.shifts[0].activities.length, 2);
});

test("conflict overlay keeps the Office version and blocks later optimistic changes", () => {
  const start = shiftCommand("start", "shift.start", { workWithoutVehicle: true });
  const office = {
    ...projectShifts(shiftSnapshot(), [start])[0],
    status: "approved",
    version: 5,
    endAt: "2026-11-09T16:00:00Z",
  };
  office.activities[0].endAt = office.endAt;
  const conflict = {
    ...shiftCommand("edit", "shift.switch", { type: "break" }),
    state: "conflict",
    checkpoint: { shift: office },
  };
  const later = shiftCommand("late", "shift.end", { endAt: "2026-11-09T18:00:00Z" });
  const result = projectShifts(shiftSnapshot(), [start, conflict, later]);
  assert.deepEqual(result, [office]);
  assert.equal(result[0].version, 5);
});

test("pending night edits replace the same night instead of adding duplicate nights", () => {
  const result = projectShifts(shiftSnapshot(), [
    shiftCommand("start", "shift.start", { workWithoutVehicle: true }),
    shiftCommand("night-one", "shift.night", { night: "2026-11-09", countryCode: "DE", kind: "cab" }),
    shiftCommand("night-two", "shift.night", { night: "2026-11-09", countryCode: "FR", kind: "hotel" }),
  ]);
  assert.equal(result[0].overnights.length, 1);
  assert.equal(result[0].overnights[0].countryCode, "FR");
  assert.equal(result[0].overnights[0].kind, "hotel");
});

test("company date and cross-midnight DST durations use instants rather than phone wall time", () => {
  assert.equal(companyDate("2026-11-09T23:30:00Z", "Europe/Berlin"), "2026-11-10");
  const shifts = projectShifts(shiftSnapshot(), [
    shiftCommand("start", "shift.start", {}, "2026-10-24T12:00:00+02:00"),
    shiftCommand("end", "shift.end", {}, "2026-10-25T12:00:00+01:00"),
  ]);
  assert.equal(shifts[0].date, "2026-10-24");
  assert.equal(shiftHours(shifts[0]), 25);
});

test("manual company time conversion refuses nonexistent and ambiguous DST times", () => {
  assert.throws(
    () => companyInstant("2026-03-29T02:30", "Europe/Berlin", "2026-03-28T00:00:00Z"),
    /invalid_time/,
  );
  assert.throws(
    () => companyInstant("2026-10-25T02:30", "Europe/Berlin", "2026-10-24T00:00:00Z"),
    /ambiguous_time/,
  );
  assert.equal(
    companyInstant("2026-11-09T12:30", "Europe/Berlin", "2026-11-09T00:00:00Z"),
    "2026-11-09T11:30:00.000Z",
  );
});

test("document replay obtains links only at flush, persists uploaded IDs, then confirms", async () => {
  const { queue } = fixture();
  const item = documentCommand();
  await queue.enqueue(item);
  const events = [];
  assert.deepEqual(await queue.list(), [item]);
  await replayDocument(item, {
    upload: async (type, photo) => {
      events.push("upload");
      assert.equal(type, "code95");
      assert.equal(photo.data, "AAECAw==");
      return "pending-one";
    },
    checkpoint: async (id, checkpoint) => {
      events.push("checkpoint");
      await queue.checkpoint(id, checkpoint);
    },
    confirm: async (id, payload, files) => {
      events.push("confirm");
      assert.equal(id, item.commandId);
      assert.deepEqual(payload, item.payload);
      assert.deepEqual((await queue.list())[0].checkpoint.files, files);
    },
  });
  assert.deepEqual(events, ["upload", "checkpoint", "confirm"]);
  await queue.acknowledge([item.commandId]);
  assert.deepEqual(await queue.list(), []);
});

test("lost confirmation response retains encrypted-envelope photos and retry confirms without another upload", async () => {
  const { queue, storage } = fixture();
  const item = documentCommand();
  await queue.enqueue(item);
  let uploads = 0;
  await assert.rejects(
    replayDocument(item, {
      upload: async () => {
        uploads += 1;
        return "pending-one";
      },
      checkpoint: queue.checkpoint,
      confirm: async () => {
        throw new Error("response_lost");
      },
    }),
    /response_lost/,
  );
  const reopened = createCommandQueue(storage);
  const saved = (await reopened.list())[0];
  assert.equal(saved.payload.photos[0].data, item.payload.photos[0].data);
  await replayDocument(saved, {
    upload: async () => {
      uploads += 1;
      return "unexpected";
    },
    checkpoint: reopened.checkpoint,
    confirm: async (id, payload, files) => {
      assert.equal(id, item.commandId);
      assert.deepEqual(files, [{ fileId: "pending-one", page: "page" }]);
    },
  });
  assert.equal(uploads, 1);
  await reopened.acknowledge([item.commandId]);
  assert.deepEqual(await reopened.list(), []);
});

test("expired unconfirmed files are retried before replacement links are requested", async () => {
  const { queue } = fixture();
  const item = { ...documentCommand(), checkpoint: { files: [{ fileId: "expired", page: "page" }] } };
  await queue.enqueue(item);
  const events = [];
  await replayDocument(item, {
    upload: async () => {
      events.push("fresh-upload");
      return "fresh";
    },
    checkpoint: async (id, checkpoint) => {
      events.push(checkpoint ? "checkpoint" : "clear");
      await queue.checkpoint(id, checkpoint);
    },
    confirm: async (id, payload, files) => {
      events.push(`confirm:${files[0].fileId}`);
      if (files[0].fileId === "expired") throw new Error("file_rejected");
    },
  });
  assert.deepEqual(events, ["confirm:expired", "clear", "fresh-upload", "checkpoint", "confirm:fresh"]);
});

test("checkpoint storage failure prevents confirmation and preserves source photos", async () => {
  const item = documentCommand();
  let confirms = 0;
  await assert.rejects(
    replayDocument(item, {
      upload: async () => "pending-one",
      checkpoint: async () => {
        throw new Error("quota_exceeded");
      },
      confirm: async () => {
        confirms += 1;
      },
    }),
    /quota_exceeded/,
  );
  assert.equal(confirms, 0);
  assert.equal(item.payload.photos[0].data, "AAECAw==");
});

test("transient confirmation failures never request replacement uploads", async () => {
  const item = { ...documentCommand(), checkpoint: { files: [{ fileId: "pending-one", page: "page" }] } };
  let uploads = 0;
  let checkpointWrites = 0;
  await assert.rejects(
    replayDocument(item, {
      upload: async () => {
        uploads += 1;
        return "unexpected";
      },
      checkpoint: async () => {
        checkpointWrites += 1;
      },
      confirm: async () => {
        throw new Error("network_error");
      },
    }),
    /network_error/,
  );
  assert.equal(uploads, 0);
  assert.equal(checkpointWrites, 0);
});

test("a second queued document of the same type cannot displace its unsent photos", async () => {
  const { queue } = fixture();
  const item = documentCommand();
  await queue.enqueue(item);
  await assert.rejects(queue.enqueue({ ...item, commandId: "another" }), /upload_already_queued/);
  assert.deepEqual(await queue.list(), [item]);
});

test("manual retry keeps uploaded file checkpoint and conflicts remain visible", async () => {
  const { queue } = fixture();
  await queue.enqueue(documentCommand());
  const checkpoint = { files: [{ fileId: "pending-one", page: "page" }] };
  await queue.checkpoint("document-one", checkpoint);
  await queue.mark("document-one", "failed", "storage_unavailable");
  await queue.retry("document-one");
  const saved = (await queue.list())[0];
  assert.equal(saved.state, "waiting");
  assert.equal(saved.errorCode, undefined);
  assert.deepEqual(saved.checkpoint, checkpoint);
  await queue.mark("document-one", "conflict", "office_changed");
  await queue.retry("document-one");
  assert.equal((await queue.list())[0].state, "conflict");
});

test("queue survives reopening with the complete command envelope and photo payload", async () => {
  const { queue, storage } = fixture();
  const original = { ...command(), type: "document.upload", payload: { photo: "AAECAw==" } };
  await queue.enqueue(original);
  const reopened = createCommandQueue(storage);
  assert.deepEqual(await reopened.list(), [original]);
});

test("ending a shift and recording its night is one atomic local write", async () => {
  const { queue, storage } = fixture();
  const end = shiftCommand("end", "shift.end", {});
  const night = shiftCommand("night", "shift.night", { countryCode: "DE", kind: "cab", night: "2026-11-09" });
  await queue.enqueueMany([end, night]);
  assert.deepEqual(await createCommandQueue(storage).list(), [end, night]);
});

test("an invalid second action cannot leave a half-saved end-and-night pair", async () => {
  const { queue } = fixture();
  await assert.rejects(
    queue.enqueueMany([command("end"), { ...command("night"), schemaVersion: 0 }]),
    /invalid_command/,
  );
  assert.deepEqual(await queue.list(), []);
});
test("parallel enqueues preserve every item in insertion order", async () => {
  const { queue } = fixture();
  await Promise.all(Array.from({ length: 40 }, (_, index) => queue.enqueue(command(String(index)))));
  assert.deepEqual(
    (await queue.list()).map((item) => item.commandId),
    Array.from({ length: 40 }, (_, index) => String(index)),
  );
});
test("duplicate command IDs do not append twice and changed payloads are refused", async () => {
  const { queue } = fixture();
  await queue.enqueue(command());
  await queue.enqueue(command());
  await assert.rejects(
    queue.enqueue({ ...command(), payload: { truckId: "different" } }),
    /command_id_reused/,
  );
  assert.deepEqual(await queue.list(), [command()]);
});
test("storage failure preserves earlier commands and does not report a new command as saved", async () => {
  const { queue, failWrites } = fixture();
  await queue.enqueue(command());
  failWrites();
  await assert.rejects(queue.enqueue(command("two")), /quota_exceeded/);
  assert.deepEqual(await queue.list(), [command()]);
});
test("server acknowledgement removes only applied IDs and retains conflicts and failures", async () => {
  const { queue } = fixture();
  await queue.enqueue(command());
  await queue.enqueue(command("two"));
  await queue.enqueue(command("three"));
  await queue.mark("two", "conflict", "office_changed");
  await queue.mark("three", "failed", "dependency");
  await queue.acknowledge(["one", "unknown"]);
  assert.deepEqual(await queue.list(), [
    { ...command("two"), state: "conflict", errorCode: "office_changed" },
    { ...command("three"), state: "failed", errorCode: "dependency" },
  ]);
});
test("old offline work is retained beyond fourteen days without eviction", async () => {
  const { queue } = fixture();
  const old = { ...command(), deviceTime: "2020-01-01T08:00:00Z" };
  await queue.enqueue(old);
  await queue.enqueue(command("two"));
  assert.deepEqual(await queue.list(), [old, command("two")]);
});
test("invalid command envelopes are rejected without writes", async () => {
  const { queue } = fixture();
  for (const override of [
    { commandId: "" },
    { aggregateId: "" },
    { type: "" },
    { schemaVersion: 0 },
    { schemaVersion: 1.5 },
    { baseVersion: -1 },
    { deviceTime: "invalid" },
    { state: "conflict" },
  ]) {
    await assert.rejects(queue.enqueue({ ...command(), ...override }), /invalid_command/);
  }
  assert.deepEqual(await queue.list(), []);
});
test("photo encoding preserves all bytes including across chunk boundaries", async () => {
  const bytes = Uint8Array.from({ length: 20000 }, (_, index) => index % 256);
  const encoded = await photoPayload(new Blob([bytes]));
  assert.deepEqual(Buffer.from(encoded, "base64"), Buffer.from(bytes));
});
test("photo compression resizes and retries until the stored image is below 1.5 MB", async () => {
  const draws = [];
  let closed = false;
  let attempts = 0;
  globalThis.createImageBitmap = async () => ({
    width: 4000,
    height: 3000,
    close: () => {
      closed = true;
    },
  });
  globalThis.document = {
    createElement: () => ({
      getContext: () => ({ drawImage: (...args) => draws.push(args) }),
      toBlob: (callback) =>
        callback(new Blob([new Uint8Array(++attempts === 1 ? 1_600_000 : 900_000)], { type: "image/jpeg" })),
    }),
  };
  try {
    const result = await compressPhoto(new Blob(["original"], { type: "image/png" }), 10);
    assert.equal(result.size, 900_000);
    assert.equal(result.type, "image/jpeg");
    assert.equal(attempts, 2);
    assert.deepEqual(draws[0].slice(1, 5), [400, 300, 3200, 2400]);
    assert.ok(draws[1][7] < draws[0][7]);
    assert.equal(closed, true);
  } finally {
    delete globalThis.document;
    delete globalThis.createImageBitmap;
  }
});
test("invalid photos and crop values are rejected before decoding", async () => {
  await assert.rejects(compressPhoto(new Blob(["data"], { type: "text/plain" })), /invalid_photo/);
  await assert.rejects(compressPhoto(new Blob(["data"], { type: "image/png" }), 21), /invalid_photo/);
});
test("low storage requests persistence then refuses a photo without deleting queued work", async () => {
  let requested = false;
  const previous = Object.getOwnPropertyDescriptor(globalThis, "navigator");
  Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: {
      storage: {
        persist: async () => {
          requested = true;
          return false;
        },
        estimate: async () => ({ quota: 10_000_000, usage: 9_000_000 }),
      },
    },
  });
  try {
    await assert.rejects(ensurePhotoSpace(1_500_000), /photo_storage_low/);
    assert.equal(requested, true);
  } finally {
    if (previous) Object.defineProperty(globalThis, "navigator", previous);
    else delete globalThis.navigator;
  }
});
