import { localStore } from "./localStore";
import { decryptRecord, storeEncryptedRecord, type EncryptedRecord } from "./pinVault";
import type { ShiftSnapshot } from "./shiftProjection";

const key = "driver-shifts";
export async function readShifts(): Promise<ShiftSnapshot | undefined> {
  const record = await localStore.get<EncryptedRecord>(key);
  return record ? decryptRecord<ShiftSnapshot>(record) : undefined;
}
export async function replaceShifts(snapshot: ShiftSnapshot): Promise<void> {
  await storeEncryptedRecord(key, snapshot);
  window.dispatchEvent(new Event("fleetops:shifts-changed"));
}
