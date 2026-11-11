import { localStore } from "./localStore";
import {
  createVault,
  decryptRecord,
  isUnlocked,
  storeEncryptedRecord,
  type EncryptedRecord,
  type VaultConfig,
} from "./pinVault";

export async function resetVaultPin(pin: string, config: VaultConfig) {
  const saved = new Map<string, unknown>();
  if (isUnlocked()) {
    for (const key of ["driver-snapshot", "driver-command-queue", "driver-shifts", "driver-last-defect"]) {
      const record = await localStore.get<EncryptedRecord>(key);
      if (record) saved.set(key, await decryptRecord(record));
    }
  }
  await createVault(pin, { ...config, pinResetRequired: false });
  for (const [key, value] of saved) await storeEncryptedRecord(key, value);
}
