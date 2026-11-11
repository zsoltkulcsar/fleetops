import { apiClient } from "@fleetops/api-client";
import { DriverRequestError, scheduleApi } from "./scheduleApi";
import { commandQueue } from "./storage/commandQueue";
import { replayDefect } from "./storage/defectReplay";

export async function flushDefects() {
  if (!navigator.onLine) return;
  await navigator.locks.request("fleetops-driver-defect-sync", { ifAvailable: true }, async (lock) => {
    if (!lock) return;
    const blocked = new Set<string>();
    for (const command of await commandQueue.list()) {
      if (!command.type.startsWith("defect.")) continue;
      if (command.state !== "waiting" || blocked.has(command.aggregateId)) {
        blocked.add(command.aggregateId);
        continue;
      }
      try {
        const result = await replayDefect(command, {
          checkpoint: commandQueue.checkpoint,
          send: scheduleApi.commands,
          upload: async (photo) => {
            const bytes = Uint8Array.from(atob(photo.data), (character) => character.charCodeAt(0));
            const grant = await apiClient.POST("/api/v1/driver/defects/upload-links", {
              body: { contentType: photo.contentType, sizeBytes: bytes.length },
            });
            if (!grant.response.ok || !grant.data) throw new DriverRequestError(grant.response.status);
            const response = await fetch(grant.data.url.url, {
              method: "PUT",
              headers: grant.data.requiredHeaders,
              body: new Blob([bytes], { type: photo.contentType }),
              credentials: "omit",
            });
            if (!response.ok) throw new Error("photo_upload_failed");
            return grant.data.fileId;
          },
        });
        if (result.status === "applied") {
          await commandQueue.acknowledge([command.commandId]);
          window.dispatchEvent(new Event("fleetops:defect-synced"));
        } else if (result.status === "failed" || result.status === "conflict") {
          await commandQueue.mark(command.commandId, result.status, result.code ?? "defect_refused");
          blocked.add(command.aggregateId);
        } else throw new Error("invalid_batch_result");
      } catch (failure) {
        if (failure instanceof DriverRequestError && failure.status === 401) return;
        if (
          failure instanceof DriverRequestError &&
          failure.status >= 400 &&
          failure.status < 500 &&
          ![408, 429].includes(failure.status)
        ) {
          await commandQueue.mark(command.commandId, "failed", failure.code ?? "defect_refused");
          blocked.add(command.aggregateId);
          continue;
        }
        throw failure;
      }
    }
  });
}
