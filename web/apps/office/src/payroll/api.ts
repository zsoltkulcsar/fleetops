import { apiClient, type components } from "@fleetops/api-client";

export type Schema = components["schemas"];
export class PayrollError extends Error {
  constructor(
    public code: string,
    public shifts: Schema["PayrollShiftVersion"][] = [],
  ) {
    super(code);
  }
}
export async function checked<T>(result: { data?: T; error?: unknown; response: Response }): Promise<T> {
  if (!result.response.ok || result.data === undefined) {
    const problem = result.error;
    const code: unknown = problem && typeof problem === "object" ? Reflect.get(problem, "code") : undefined;
    const shifts: unknown =
      problem && typeof problem === "object" ? Reflect.get(problem, "shifts") : undefined;
    throw new PayrollError(
      typeof code === "string" ? code : "request_failed",
      Array.isArray(shifts)
        ? shifts.filter(
            (item): item is Schema["PayrollShiftVersion"] =>
              item && typeof item.shiftId === "string" && typeof item.version === "number",
          )
        : [],
    );
  }
  return result.data;
}
export const payrollApi = {
  readiness: (month: string) =>
    apiClient
      .GET("/api/v1/payroll/readiness", { params: { query: { month } }, cache: "no-store" })
      .then(checked),
  preview: (month: string) =>
    apiClient
      .GET("/api/v1/payroll/preview", { params: { query: { month } }, cache: "no-store" })
      .then(checked),
  history: (month: string) =>
    apiClient
      .GET("/api/v1/payroll/exports", { params: { query: { month } }, cache: "no-store" })
      .then(checked),
  generate: (body: Schema["GeneratePayrollCommand"]) =>
    apiClient.POST("/api/v1/payroll/exports", { body }).then(checked),
  sent: (id: string, body: Schema["PayrollSentRequest"]) =>
    apiClient.POST("/api/v1/payroll/exports/{id}/sent", { params: { path: { id } }, body }).then(checked),
  discard: (id: string, reason: string) =>
    apiClient
      .POST("/api/v1/payroll/exports/{id}/discard", { params: { path: { id } }, body: { reason } })
      .then(checked),
  download: (id: string) =>
    apiClient.POST("/api/v1/payroll/exports/{id}/download-link", { params: { path: { id } } }).then(checked),
  reopen: (id: string, body: Schema["PayrollReopenRequest"]) =>
    apiClient.POST("/api/v1/shifts/{id}/reopen", { params: { path: { id } }, body }).then(checked),
};
