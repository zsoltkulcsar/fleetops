import { DriverRequestError, scheduleApi } from "./scheduleApi";
import { commandQueue } from "./storage/commandQueue";
import { replayDocument } from "./storage/documentReplay";

export async function flushDocuments(): Promise<void> {
  if (!navigator.onLine) return;
  await navigator.locks.request("fleetops-driver-document-sync", { ifAvailable: true }, async (lock) => {
    if (!lock) return;
    const commands = (await commandQueue.list())
      .filter((command) => command.type === "document.upload" && command.state === "waiting")
      .slice(0, 20);
    const blocked = new Set<string>();
    let transientFailure = false;
    for (const command of commands) {
      if (!navigator.onLine) return;
      if (command.state !== "waiting" || blocked.has(command.aggregateId)) {
        blocked.add(command.aggregateId);
        continue;
      }
      if (command.type !== "document.upload") continue;
      try {
        await replayDocument(command, {
          upload: async (type, photo) => {
            const binary = atob(photo.data);
            const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
            const grant = await scheduleApi.upload({
              type,
              contentType: photo.contentType,
              sizeBytes: bytes.length,
            });
            const response = await fetch(grant.url.url, {
              method: "PUT",
              headers: grant.requiredHeaders,
              body: new Blob([bytes], { type: photo.contentType }),
              credentials: "omit",
            });
            if (!response.ok) throw new Error("photo_upload_failed");
            return grant.fileId;
          },
          checkpoint: commandQueue.checkpoint,
          confirm: async (commandId, payload, files, deviceTime) => {
            await scheduleApi.confirm({
              commandId,
              deviceTime,
              schemaVersion: 1,
              type: payload.type,
              number: payload.number,
              expiresAt: payload.expiresAt,
              issuedAt: null,
              categories: null,
              files,
            });
          },
        });
        await commandQueue.acknowledge([command.commandId]);
        window.dispatchEvent(new Event("fleetops:document-synced"));
      } catch (failure) {
        blocked.add(command.aggregateId);
        if (failure instanceof DriverRequestError && failure.status === 401) return;
        if (
          failure instanceof DriverRequestError &&
          failure.status >= 400 &&
          failure.status < 500 &&
          ![408, 429].includes(failure.status)
        ) {
          await commandQueue.mark(command.commandId, "failed", failure.code ?? "document_refused");
        } else if (
          failure instanceof Error &&
          ["unsupported_schema_version", "invalid_document_upload"].includes(failure.message)
        ) {
          await commandQueue.mark(command.commandId, "failed", failure.message);
        } else {
          transientFailure = true;
        }
        // Transient failures keep the durable command waiting for reconnect/retry.
      }
    }
    if (transientFailure) throw new Error("sync_unavailable");
  });
}
