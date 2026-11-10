import { localStore } from "./localStore";
import { decryptRecord, storeEncryptedRecord, type EncryptedRecord } from "./pinVault";

import { createCommandQueue } from "./commandQueueCore";
import type { QueuedCommand } from "./commandQueueCore";
export type { QueuedCommand } from "./commandQueueCore";

export const queueKey = "driver-command-queue";
export const commandQueue = createCommandQueue({
  read: async () => {
    const record = await localStore.get<EncryptedRecord>(queueKey);
    return record ? decryptRecord<QueuedCommand[]>(record) : [];
  },
  write: async (commands) => {
    await storeEncryptedRecord(queueKey, commands);
    window.dispatchEvent(new Event("fleetops:queue-changed"));
  },
  exclusive: async (operation) => await navigator.locks.request("fleetops-driver-command-queue", operation),
});

export function newCommand(
  type: string,
  aggregateId: string,
  baseVersion: number,
  payload: unknown,
): QueuedCommand {
  return {
    commandId: crypto.randomUUID(),
    type,
    schemaVersion: 1,
    aggregateId,
    baseVersion,
    deviceTime: new Date().toISOString(),
    payload,
    state: "waiting",
  };
}
