import { apiClient, type components } from "@fleetops/api-client";
import { AuthRequestError } from "../auth/api";
export type Schema = components["schemas"];
export async function checked<T>(result: { data?: T; response: Response }): Promise<T> {
  if (!result.response.ok || result.data === undefined) throw new AuthRequestError(result.response.status);
  return result.data;
}
export const timesheetsApi = {
  week: (week: string, filter: string, search: string, page: number) =>
    apiClient
      .GET("/api/v1/timesheets/week", {
        params: { query: { week, filter, search, page, pageSize: 50 } },
        cache: "no-store",
      })
      .then(checked),
  detail: (shiftId: string) =>
    apiClient
      .GET("/api/v1/timesheets/shifts/{shiftId}", { params: { path: { shiftId } }, cache: "no-store" })
      .then(checked),
  approve: (items: Schema["BulkApprovalItem"][]) =>
    apiClient.POST("/api/v1/timesheets/approve", { body: { items } }).then(checked),
  reject: (shiftId: string, body: Schema["RejectShiftRequest"]) =>
    apiClient
      .POST("/api/v1/timesheets/shifts/{shiftId}/reject", { params: { path: { shiftId } }, body })
      .then(checked),
  edit: (shiftId: string, body: Schema["EditShiftRequest"]) =>
    apiClient
      .PUT("/api/v1/timesheets/shifts/{shiftId}", { params: { path: { shiftId } }, body })
      .then(checked),
  add: (body: Schema["AddShiftRequest"]) =>
    apiClient.POST("/api/v1/timesheets/shifts", { body }).then(checked),
  end: (shiftId: string, body: Schema["EndShiftRequest"]) =>
    apiClient
      .POST("/api/v1/timesheets/shifts/{shiftId}/end", { params: { path: { shiftId } }, body })
      .then(checked),
};
