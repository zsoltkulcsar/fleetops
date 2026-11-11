import { apiClient, type components } from "@fleetops/api-client";
import { checked } from "../people/api";

export type Attention = components["schemas"]["AttentionResult"];
export type AttentionGroup = components["schemas"]["AttentionGroup"];
export type AttentionItem = components["schemas"]["AttentionItem"];

export const attentionApi = {
  list: () => apiClient.GET("/api/v1/attention").then(checked),
  snooze: (id: string, body: components["schemas"]["SnoozeAlertRequest"]) =>
    apiClient.POST("/api/v1/alerts/{id}/snooze", { params: { path: { id } }, body }).then(checked),
  unsnooze: (id: string, version: components["schemas"]["UnsnoozeAlertRequest"]["version"]) =>
    apiClient
      .POST("/api/v1/alerts/{id}/unsnooze", { params: { path: { id } }, body: { version } })
      .then(checked),
};
