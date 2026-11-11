import { DriverRequestError, scheduleApi } from "./scheduleApi";
import { commandQueue } from "./storage/commandQueue";
import { replaceSnapshot } from "./storage/snapshot";

export async function syncPlanning() {
  if (!navigator.onLine) return;
  await navigator.locks.request("fleetops-driver-planning-sync", { ifAvailable: true }, async (lock) => {
    if (!lock) return;
    for (const command of await commandQueue.list()) {
      if (!command.type.startsWith("absence.") && !command.type.startsWith("schedule.request")) continue;
      if (command.state !== "waiting") continue;
      try {
        const { commandId, type, schemaVersion, aggregateId, baseVersion, deviceTime, payload } = command;
        const response = await scheduleApi.commands([
          { commandId, type, schemaVersion, aggregateId, baseVersion, deviceTime, payload },
        ]);
        const result = response.results[0];
        if (response.results.length !== 1 || result?.commandId !== commandId)
          throw new Error("invalid_batch_result");
        if (result.status === "applied") {
          await replaceSnapshot(await scheduleApi.snapshot());
          await commandQueue.acknowledge([commandId]);
          window.dispatchEvent(new Event("fleetops:planning-synced"));
        } else if (result.status === "failed" || result.status === "conflict") {
          await commandQueue.checkpoint(commandId, result);
          await commandQueue.mark(commandId, result.status, result.code ?? "planning_refused");
        } else throw new Error("invalid_batch_result");
      } catch (failure) {
        if (failure instanceof DriverRequestError && failure.status === 401) return;
        throw failure;
      }
    }
  });
}
