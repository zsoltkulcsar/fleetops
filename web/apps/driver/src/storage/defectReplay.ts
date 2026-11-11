import type { components } from "@fleetops/api-client";
import type { QueuedCommand } from "./commandQueueCore";
import type { DocumentUploadPayload } from "./documentReplay";

// Photos stay in the encrypted queue; only uploaded file IDs enter the API payload.
export type LocalDefectReport = Pick<
  components["schemas"]["LogOfficeDefectCommand"],
  "vehicleId" | "category" | "severity" | "description"
> & {
  photos: DocumentUploadPayload["photos"];
};

export async function replayDefect(
  command: QueuedCommand,
  port: {
    upload: (photo: LocalDefectReport["photos"][number]) => Promise<string>;
    checkpoint: (id: string, value: unknown) => Promise<void>;
    send: (
      commands: components["schemas"]["DriverCommand"][],
    ) => Promise<components["schemas"]["DriverCommandBatchResult"]>;
  },
) {
  if (command.schemaVersion !== 1) throw new Error("unsupported_schema_version");
  let payload = command.payload;
  if (command.type === "defect.report") {
    const report = command.payload as LocalDefectReport;
    let photos = command.checkpoint as string[] | undefined;
    if (!photos) {
      photos = [];
      for (const photo of report.photos) photos.push(await port.upload(photo));
      await port.checkpoint(command.commandId, photos);
    }
    payload = { ...report, photos };
  }
  const { commandId, type, schemaVersion, aggregateId, baseVersion, deviceTime } = command;
  const response = await port.send([
    { commandId, type, schemaVersion, aggregateId, baseVersion, deviceTime, payload },
  ]);
  const result = response.results[0];
  if (response.results.length !== 1 || result?.commandId !== command.commandId)
    throw new Error("incomplete_batch_result");
  return result;
}
