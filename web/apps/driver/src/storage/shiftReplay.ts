import type { components } from "@fleetops/api-client";
import type { QueuedCommand } from "./commandQueueCore";
import type { ShiftSnapshot } from "./shiftProjection";

type Result = components["schemas"]["DriverCommandResult"];
export type ShiftReplayPort = {
  list: () => Promise<QueuedCommand[]>;
  send: (commands: components["schemas"]["DriverCommand"][]) => Promise<{ results: Result[] }>;
  checkpoint: (commandId: string, value: unknown) => Promise<void>;
  mark: (commandId: string, state: "failed" | "conflict", code: string) => Promise<void>;
  snapshot: () => Promise<ShiftSnapshot>;
  replace: (snapshot: ShiftSnapshot) => Promise<void>;
  acknowledge: (ids: readonly string[]) => Promise<void>;
};

export async function replayShifts(port: ShiftReplayPort): Promise<void> {
  const sent = new Set<string>();
  for (let batch = 0; batch < 25; batch += 1) {
    const pending = (await port.list())
      .filter(
        (command) =>
          (command.type.startsWith("shift.") || command.type === "correction.request") &&
          command.state === "waiting" &&
          !sent.has(command.commandId),
      )
      .slice(0, 20);
    if (!pending.length) break;
    const response = await port.send(
      pending.map(({ commandId, type, schemaVersion, aggregateId, baseVersion, deviceTime, payload }) => ({
        commandId,
        type,
        schemaVersion,
        aggregateId,
        baseVersion,
        deviceTime,
        payload,
      })),
    );
    const expected = new Set(pending.map((command) => command.commandId));
    if (response.results.length !== pending.length) throw new Error("incomplete_batch_result");
    for (const result of response.results) {
      if (!expected.delete(result.commandId) || !["applied", "failed", "conflict"].includes(result.status))
        throw new Error("invalid_batch_result");
      sent.add(result.commandId);
      if (result.status === "failed" || result.status === "conflict") {
        await port.checkpoint(result.commandId, result);
        await port.mark(result.commandId, result.status, result.code ?? "sync_failed");
      }
    }
  }
  const snapshot = await port.snapshot();
  // Acknowledgement follows the durable snapshot write. Until then the old
  // snapshot plus the command queue remains a complete local representation.
  await port.replace(snapshot);
  await port.acknowledge(snapshot.appliedCommandIds);
}
