import type { components } from "@fleetops/api-client";
import { localStore } from "./localStore";

export type VaultConfig = components["schemas"]["DriverConfig"];
export type Vault = {
  config: VaultConfig;
  salt: Uint8Array<ArrayBuffer>;
  iv: Uint8Array<ArrayBuffer>;
  wrappedKey?: ArrayBuffer;
  failedTries: number;
  locked: boolean;
  lastOnlineAt: number;
};

let dataKey: CryptoKey | undefined;
const vaultId = "pin-vault";
const exclusive = <T>(operation: () => Promise<T>) =>
  navigator.locks.request("fleetops-driver-vault", operation);

function validConfig(config: VaultConfig | undefined): config is VaultConfig {
  return (
    !!config &&
    Number.isInteger(config.pinLength) &&
    config.pinLength > 0 &&
    Number.isInteger(config.maxPinTries) &&
    config.maxPinTries > 0 &&
    Number.isFinite(config.maxOfflineDays) &&
    config.maxOfflineDays > 0 &&
    typeof config.officePhone === "string" &&
    config.officePhone.length > 0 &&
    typeof config.name === "string" &&
    config.name.length > 0
  );
}

export async function readVault() {
  const vault = await localStore.get<Vault>(vaultId);
  if (
    vault &&
    (!validConfig(vault.config) ||
      !Number.isInteger(vault.failedTries) ||
      vault.failedTries < 0 ||
      typeof vault.locked !== "boolean" ||
      !(vault.salt instanceof Uint8Array) ||
      vault.salt.length !== 16 ||
      !(vault.iv instanceof Uint8Array) ||
      vault.iv.length !== 12)
  ) {
    lockVault();
    throw new Error("invalid_vault");
  }
  return vault;
}
export const expired = (vault: Vault) =>
  !Number.isFinite(vault.lastOnlineAt) ||
  Date.now() - vault.lastOnlineAt > vault.config.maxOfflineDays * 86_400_000;

async function wrappingKey(pin: string, salt: Uint8Array<ArrayBuffer>) {
  const material = await crypto.subtle.importKey("raw", new TextEncoder().encode(pin), "PBKDF2", false, [
    "deriveKey",
  ]);
  return crypto.subtle.deriveKey(
    { name: "PBKDF2", hash: "SHA-256", salt, iterations: 600_000 },
    material,
    { name: "AES-GCM", length: 256 },
    false,
    ["wrapKey", "unwrapKey"],
  );
}

export async function createVault(pin: string, config: VaultConfig): Promise<void> {
  await exclusive(async () => {
    if (!validConfig(config) || !new RegExp(`^[0-9]{${config.pinLength}}$`).test(pin)) {
      throw new Error("invalid_config_or_pin");
    }
    const salt = crypto.getRandomValues(new Uint8Array(16));
    await localStore.remove("driver-snapshot");
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const generatedKey = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, true, [
      "encrypt",
      "decrypt",
    ]);
    const key = await wrappingKey(pin, salt);
    const wrappedKey = await crypto.subtle.wrapKey("raw", generatedKey, key, { name: "AES-GCM", iv });
    await localStore.set<Vault>(vaultId, {
      config,
      salt,
      iv,
      wrappedKey,
      failedTries: 0,
      locked: false,
      lastOnlineAt: Date.now(),
    });
    dataKey = await crypto.subtle.unwrapKey(
      "raw",
      wrappedKey,
      key,
      { name: "AES-GCM", iv },
      "AES-GCM",
      false,
      ["encrypt", "decrypt"],
    );
  });
}

export type UnlockResult = "unlocked" | "wrong" | "locked" | "expired" | "missing";
export async function unlock(pin: string): Promise<UnlockResult> {
  return exclusive(async () => {
    dataKey = undefined;
    const vault = await readVault();
    if (!vault) return "missing";
    if (vault.locked || !vault.wrappedKey || vault.failedTries >= vault.config.maxPinTries) {
      vault.locked = true;
      delete vault.wrappedKey;
      await localStore.set(vaultId, vault);
      await localStore.remove("driver-snapshot");
      return "locked";
    }
    if (expired(vault)) return "expired";
    vault.failedTries += 1;
    // Commit the attempt before doing any expensive crypto, even if the tab closes.
    await localStore.set(vaultId, vault);
    let unwrapped: CryptoKey;
    try {
      const key = await wrappingKey(pin, vault.salt);
      unwrapped = await crypto.subtle.unwrapKey(
        "raw",
        vault.wrappedKey,
        key,
        { name: "AES-GCM", iv: vault.iv },
        "AES-GCM",
        false,
        ["encrypt", "decrypt"],
      );
    } catch {
      if (vault.failedTries >= vault.config.maxPinTries) {
        vault.locked = true;
        delete vault.wrappedKey;
        await localStore.remove("driver-snapshot");
      }
      await localStore.set(vaultId, vault);
      return vault.locked ? "locked" : "wrong";
    }
    vault.failedTries = 0;
    await localStore.set(vaultId, vault);
    dataKey = unwrapped;
    return "unlocked";
  });
}

export const lockVault = () => {
  dataKey = undefined;
};
export const isUnlocked = () => dataKey !== undefined;
export const eraseVault = () =>
  exclusive(async () => {
    lockVault();
    await localStore.remove(vaultId);
    await localStore.remove("driver-snapshot");
  });

export type EncryptedRecord = { iv: Uint8Array<ArrayBuffer>; ciphertext: ArrayBuffer };
export async function encryptRecord(value: unknown): Promise<EncryptedRecord> {
  const key = dataKey;
  if (!key) throw new Error("vault_locked");
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const ciphertext = await crypto.subtle.encrypt(
    { name: "AES-GCM", iv },
    key,
    new TextEncoder().encode(JSON.stringify(value)),
  );
  if (key !== dataKey) throw new Error("vault_locked");
  return { iv, ciphertext };
}
export async function decryptRecord<T>(record: EncryptedRecord): Promise<T> {
  const key = dataKey;
  if (!key) throw new Error("vault_locked");
  const plaintext = await crypto.subtle.decrypt({ name: "AES-GCM", iv: record.iv }, key, record.ciphertext);
  if (key !== dataKey) throw new Error("vault_locked");
  return JSON.parse(new TextDecoder().decode(plaintext)) as T;
}
export async function storeEncryptedRecord(key: string, value: unknown): Promise<void> {
  await exclusive(async () => {
    const record = await encryptRecord(value);
    if (!isUnlocked()) throw new Error("vault_locked");
    await localStore.set(key, record);
  });
}
export const markOnline = () =>
  exclusive(async () => {
    const vault = await readVault();
    if (vault && !vault.locked) {
      vault.lastOnlineAt = Date.now();
      await localStore.set(vaultId, vault);
    }
  });
