import type { components } from "@fleetops/api-client";
import type { QueuedCommand } from "./commandQueueCore";

export type DocumentUploadPayload = {
  type: components["schemas"]["DocumentType"];
  number: string | null;
  expiresAt: string | null;
  photos: { page: components["schemas"]["DocumentFilePage"]; contentType: string; data: string }[];
};
type ConfirmedFile = components["schemas"]["ConfirmFileInput"];
type Checkpoint = { files: ConfirmedFile[] };
export type DocumentReplayPort = {
  upload: (
    type: DocumentUploadPayload["type"],
    photo: DocumentUploadPayload["photos"][number],
  ) => Promise<string>;
  confirm: (
    commandId: string,
    payload: DocumentUploadPayload,
    files: ConfirmedFile[],
    deviceTime: string,
  ) => Promise<void>;
  checkpoint: (commandId: string, checkpoint: Checkpoint | null) => Promise<void>;
};

export async function replayDocument(command: QueuedCommand, port: DocumentReplayPort): Promise<void> {
  if (command.schemaVersion !== 1) throw new Error("unsupported_schema_version");
  const payload = command.payload as DocumentUploadPayload;
  if (!payload?.photos?.length || payload.photos.length > 2) throw new Error("invalid_document_upload");
  let checkpoint = command.checkpoint as Checkpoint | null | undefined;
  if (checkpoint) {
    try {
      // Always retry an ambiguous confirmation before uploading again. The server
      // recognises the command ID even after the original pending files expire.
      await port.confirm(command.commandId, payload, checkpoint.files, command.deviceTime);
      return;
    } catch (failure) {
      if (!(failure instanceof Error) || !["file_rejected", "file_not_found"].includes(failure.message))
        throw failure;
      await port.checkpoint(command.commandId, null);
      checkpoint = null;
    }
  }
  const files: ConfirmedFile[] = [];
  for (const photo of payload.photos) {
    const fileId = await port.upload(payload.type, photo);
    files.push({ fileId, page: photo.page });
  }
  // Commit uploaded IDs before the request that can create the document.
  await port.checkpoint(command.commandId, { files });
  await port.confirm(command.commandId, payload, files, command.deviceTime);
}
