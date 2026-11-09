import { apiClient, type paths } from "@fleetops/api-client";
import { AuthRequestError } from "../auth/api";

async function checked<T>(result: { data?: T; error?: unknown; response: Response }): Promise<T> {
  if (!result.response.ok || !result.data) {
    throw new AuthRequestError(result.response.status, result.error as { code?: string; fields?: string[] });
  }
  return result.data;
}

export const scheduleApi = {
  list: (from: string, to: string) =>
    apiClient.GET("/api/v1/schedule", { params: { query: { from, to } }, cache: "no-store" }).then(checked),
  add: (body: paths["/api/v1/schedule/cycles"]["post"]["requestBody"]["content"]["application/json"]) =>
    apiClient.POST("/api/v1/schedule/cycles", { body }).then(checked),
  edit: (
    id: string,
    body: paths["/api/v1/schedule/cycles/{id}"]["put"]["requestBody"]["content"]["application/json"],
  ) => apiClient.PUT("/api/v1/schedule/cycles/{id}", { params: { path: { id } }, body }).then(checked),
  pattern: (id: string) =>
    apiClient
      .GET("/api/v1/people/{id}/cycle-pattern", { params: { path: { id } }, cache: "no-store" })
      .then(checked),
  savePattern: (
    id: string,
    body: paths["/api/v1/people/{id}/cycle-pattern"]["put"]["requestBody"]["content"]["application/json"],
  ) => apiClient.PUT("/api/v1/people/{id}/cycle-pattern", { params: { path: { id } }, body }).then(checked),
};
export type ScheduleCycle = Awaited<ReturnType<typeof scheduleApi.list>>["items"][number];
