import { apiClient, type components, type paths } from "@fleetops/api-client";
import { AuthRequestError } from "../auth/api";

export async function checked<T>(result: { data?: T; error?: unknown; response: Response }): Promise<T> {
  if (!result.response.ok || !result.data)
    throw new AuthRequestError(result.response.status, result.error as { code?: string; fields?: string[] });
  return result.data;
}
export const defectApi = {
  list: (query: NonNullable<paths["/api/v1/fleet/defects"]["get"]["parameters"]["query"]>) =>
    apiClient.GET("/api/v1/fleet/defects", { params: { query }, cache: "no-store" }).then(checked),
  detail: (id: string) =>
    apiClient
      .GET("/api/v1/fleet/defects/{id}", { params: { path: { id } }, cache: "no-store" })
      .then(checked),
  log: (body: components["schemas"]["LogOfficeDefectCommand"]) =>
    apiClient.POST("/api/v1/fleet/defects", { body }).then(checked),
  confirm: (id: string) =>
    apiClient.POST("/api/v1/fleet/defects/{id}/confirm", { params: { path: { id } } }).then(checked),
  downgrade: (id: string, body: components["schemas"]["DowngradeDefectRequest"]) =>
    apiClient.POST("/api/v1/fleet/defects/{id}/downgrade", { params: { path: { id } }, body }).then(checked),
  close: (id: string, body: components["schemas"]["CloseDefectRequest"]) =>
    apiClient.POST("/api/v1/fleet/defects/{id}/close", { params: { path: { id } }, body }).then(checked),
  repairs: (vehicleId?: string, status?: components["schemas"]["MaintenanceStatus"]) =>
    apiClient
      .GET("/api/v1/fleet/maintenance", { params: { query: { vehicleId, status } }, cache: "no-store" })
      .then(checked),
  createRepair: (body: components["schemas"]["CreateRepairCommand"]) =>
    apiClient.POST("/api/v1/fleet/maintenance", { body }).then(checked),
  startRepair: (id: string) =>
    apiClient.POST("/api/v1/fleet/maintenance/{id}/start", { params: { path: { id } } }).then(checked),
  doneRepair: (id: string, body: components["schemas"]["DoneRepairRequest"]) =>
    apiClient.POST("/api/v1/fleet/maintenance/{id}/done", { params: { path: { id } }, body }).then(checked),
  cancelRepair: (id: string, body: components["schemas"]["CancelRepairRequest"]) =>
    apiClient.POST("/api/v1/fleet/maintenance/{id}/cancel", { params: { path: { id } }, body }).then(checked),
  drivers: (search?: string) =>
    apiClient
      .GET("/api/v1/timesheets/drivers", { params: { query: { search } }, cache: "no-store" })
      .then(checked),
  swapOptions: (employeeId: string, date: string) =>
    apiClient
      .GET("/api/v1/fleet/swap-options", { params: { query: { employeeId, date } }, cache: "no-store" })
      .then(checked),
  assign: (body: components["schemas"]["CreateAssignmentCommand"]) =>
    apiClient.POST("/api/v1/fleet/assignments", { body }).then(checked),
  editAssignment: (id: string, body: components["schemas"]["EditAssignmentRequest"]) =>
    apiClient.PUT("/api/v1/fleet/assignments/{id}", { params: { path: { id } }, body }).then(checked),
  endAssignment: (id: string) =>
    apiClient.POST("/api/v1/fleet/assignments/{id}/end", { params: { path: { id } } }).then(checked),
};
