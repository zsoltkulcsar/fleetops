import { apiClient, type components } from "@fleetops/api-client";

export class DriverRequestError extends Error {
  constructor(
    public status: number,
    public code?: string,
  ) {
    super(code ?? "driver_request_failed");
  }
}
async function checked<T>(result: { data?: T; error?: unknown; response: Response }): Promise<T> {
  if (!result.response.ok || !result.data) {
    if (result.response.status === 401) window.dispatchEvent(new Event("fleetops:driver-session-expired"));
    throw new DriverRequestError(result.response.status, (result.error as { code?: string })?.code);
  }
  return result.data;
}
export const scheduleApi = {
  snapshot: () => apiClient.GET("/api/v1/driver/snapshot", { cache: "no-store" }).then(checked),
  vehicle: (id: string) =>
    apiClient
      .GET("/api/v1/driver/vehicles/{id}", { params: { path: { id } }, cache: "no-store" })
      .then(checked),
  document: (id: string) =>
    apiClient
      .GET("/api/v1/driver/documents/{id}", { params: { path: { id } }, cache: "no-store" })
      .then(checked),
  file: (id: string, fileId: string) =>
    apiClient
      .GET("/api/v1/driver/documents/{id}/files/{fileId}/link", {
        params: { path: { id, fileId } },
        cache: "no-store",
      })
      .then(checked),
  upload: (body: { type: components["schemas"]["DocumentType"]; contentType: string; sizeBytes: number }) =>
    apiClient.POST("/api/v1/driver/documents/upload-links", { body }).then(checked),
  confirm: (body: Omit<components["schemas"]["DocumentCreateRequest"], "ownerId" | "ownerType">) =>
    apiClient.POST("/api/v1/driver/documents", { body }).then(checked),
};
