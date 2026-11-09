import type { components } from "@fleetops/api-client";
import { localStore } from "./localStore";
import { decryptRecord, storeEncryptedRecord, type EncryptedRecord } from "./pinVault";

export type DriverSnapshot = components["schemas"]["DriverSnapshot"];
const snapshotKey = "driver-snapshot";

// The allow-list is deliberate: file bodies, URLs, names of other people and
// future server additions never enter the offline record by accident.
export function snapshotFields(snapshot: DriverSnapshot): DriverSnapshot {
  return {
    employeeId: snapshot.employeeId,
    generatedAt: snapshot.generatedAt,
    canDrive: snapshot.canDrive,
    readinessCauses: snapshot.readinessCauses.map(({ code, documentType }) => ({ code, documentType })),
    cycles: snapshot.cycles.map(({ id, vehicleId, vehiclePlate, from, to, status, source, version }) => ({
      id,
      vehicleId,
      vehiclePlate,
      from,
      to,
      status,
      source,
      version,
    })),
    documents: snapshot.documents.map(({ id, type, status, expiresAt, rejectReason }) => ({
      id,
      type,
      status,
      expiresAt,
      rejectReason,
    })),
  };
}

export async function replaceSnapshot(snapshot: DriverSnapshot): Promise<void> {
  if (!snapshot.employeeId || !Number.isFinite(Date.parse(snapshot.generatedAt)))
    throw new Error("invalid_snapshot");
  await storeEncryptedRecord(snapshotKey, snapshotFields(snapshot));
}
export async function readSnapshot(): Promise<DriverSnapshot | undefined> {
  const record = await localStore.get<EncryptedRecord>(snapshotKey);
  return record ? snapshotFields(await decryptRecord<DriverSnapshot>(record)) : undefined;
}
