export type QueuedCommand = {
  commandId: string;
  type: string;
  schemaVersion: number;
  aggregateId: string;
  baseVersion: number;
  deviceTime: string;
  payload: unknown;
  state: "waiting" | "failed" | "conflict";
  errorCode?: string;
  checkpoint?: unknown;
};

export type QueueStorage = {
  read: () => Promise<QueuedCommand[]>;
  write: (commands: QueuedCommand[]) => Promise<void>;
  exclusive: <T>(operation: () => Promise<T>) => Promise<T>;
};

// Keeping the whole encrypted envelope in one committed write makes adding a
// command and its photo payload atomic. No age- or quota-based eviction runs.
export function createCommandQueue(storage: QueueStorage) {
  const enqueueMany = (input: QueuedCommand[]) => {
    const additions = structuredClone(input);
    return storage.exclusive(async () => {
      const commands = await storage.read();
      const initialLength = commands.length;
      for (const command of additions) {
        if (
          !command.commandId ||
          !command.type ||
          !command.aggregateId ||
          !Number.isInteger(command.schemaVersion) ||
          command.schemaVersion < 1 ||
          !Number.isInteger(command.baseVersion) ||
          command.baseVersion < 0 ||
          !Number.isFinite(Date.parse(command.deviceTime)) ||
          command.state !== "waiting"
        ) {
          throw new Error("invalid_command");
        }
        const existing = commands.find((item) => item.commandId === command.commandId);
        if (existing) {
          if (JSON.stringify(existing) !== JSON.stringify(command)) throw new Error("command_id_reused");
          continue;
        }
        if (
          command.type === "document.upload" &&
          commands.some((item) => item.type === command.type && item.aggregateId === command.aggregateId)
        ) {
          throw new Error("upload_already_queued");
        }
        commands.push(command);
      }
      if (commands.length !== initialLength) await storage.write(commands);
    });
  };

  return {
    list: () => storage.read(),
    checkpoint: (commandId: string, checkpoint: unknown) =>
      storage.exclusive(async () => {
        const commands = await storage.read();
        if (!commands.some((command) => command.commandId === commandId))
          throw new Error("command_not_found");
        await storage.write(
          commands.map((command) => (command.commandId === commandId ? { ...command, checkpoint } : command)),
        );
      }),
    retry: (commandId: string) =>
      storage.exclusive(async () => {
        const commands = await storage.read();
        await storage.write(
          commands.map((command) => {
            if (command.commandId !== commandId || command.state === "conflict") return command;
            const next = { ...command, state: "waiting" as const };
            delete next.errorCode;
            return next;
          }),
        );
      }),
    enqueue: (command: QueuedCommand) => enqueueMany([command]),
    enqueueMany,
    acknowledge: (appliedCommandIds: readonly string[]) =>
      storage.exclusive(async () => {
        const applied = new Set(appliedCommandIds);
        const commands = await storage.read();
        const remaining = commands.filter((command) => !applied.has(command.commandId));
        if (remaining.length !== commands.length) await storage.write(remaining);
      }),
    mark: (commandId: string, state: "failed" | "conflict", errorCode: string) =>
      storage.exclusive(async () => {
        const commands = await storage.read();
        await storage.write(
          commands.map((command) =>
            command.commandId === commandId ? { ...command, state, errorCode } : command,
          ),
        );
      }),
  };
}
