const databaseName = "fleetops-driver";
const storeName = "local";

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(storeName);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(new Error("storage_unavailable"));
    request.onblocked = () => reject(new Error("storage_blocked"));
  });
}

async function transact<T>(
  mode: IDBTransactionMode,
  operation: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  const database = await openDatabase();
  return new Promise((resolve, reject) => {
    const transaction = database.transaction(storeName, mode);
    const request = operation(transaction.objectStore(storeName));
    transaction.oncomplete = () => {
      database.close();
      resolve(request.result);
    };
    transaction.onabort = transaction.onerror = () => {
      database.close();
      reject(new Error("storage_failed"));
    };
  });
}

export const localStore = {
  clear: async (): Promise<void> => {
    await transact("readwrite", (store) => store.clear());
  },
  get: <T>(key: string) => transact<T | undefined>("readonly", (store) => store.get(key)),
  set: async <T>(key: string, value: T): Promise<void> => {
    await transact("readwrite", (store) => store.put(value, key));
  },
  remove: async (key: string): Promise<void> => {
    await transact("readwrite", (store) => store.delete(key));
  },
};
