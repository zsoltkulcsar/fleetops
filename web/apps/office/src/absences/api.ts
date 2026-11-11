import { apiClient, type components } from "@fleetops/api-client";
import { checked, peopleApi } from "../people/api";

export type AbsenceDetail = components["schemas"]["AbsenceDetail"];
export const absenceLabels: Record<components["schemas"]["AbsenceType"], string> = {
  vacation: "Vacation",
  sick: "Sick",
  training: "Training",
  unpaid: "Unpaid leave",
  time_off_in_lieu: "Time off in lieu",
  other: "Other",
};
export const statusLabels: Record<components["schemas"]["AbsenceStatus"], string> = {
  requested: "Requested",
  approved: "Approved",
  declined: "Declined",
  cancelled: "Cancelled",
};
export class AbsenceError extends Error {
  constructor(
    public code: string,
    public shifts: unknown[],
    public fields: string[],
  ) {
    super(code);
  }
}
async function checkAbsence<T>(result: { data?: T; response: Response; error?: unknown }): Promise<T> {
  if (result.response.ok && result.data !== undefined) return result.data;
  const problem = result.error;
  const read = (key: string): unknown =>
    problem && typeof problem === "object" ? Reflect.get(problem, key) : undefined;
  const code = read("code");
  const shifts = read("shifts");
  const fields = read("fields");
  throw new AbsenceError(
    typeof code === "string" ? code : "request_failed",
    Array.isArray(shifts) ? shifts : [],
    Array.isArray(fields) ? fields.filter((field): field is string => typeof field === "string") : [],
  );
}
export const absencesApi = {
  holidays: (from: string, to: string) =>
    apiClient.GET("/api/v1/holidays", { params: { query: { from, to } } }).then(checked),
  balance: (id: string, year: number) =>
    apiClient
      .GET("/api/v1/people/{id}/vacation-balance", { params: { path: { id }, query: { year } } })
      .then(checked),
  people: async (role?: string) => {
    const people: components["schemas"]["PersonRow"][] = [];
    for (let page = 1; ; page += 1) {
      const result = await peopleApi.list({ page, pageSize: 100, role });
      people.push(...result.items);
      if (people.length >= result.total || !result.items.length) return people;
    }
  },
  list: (from: string, to: string, employeeId?: string) =>
    apiClient.GET("/api/v1/absences", { params: { query: { from, to, employeeId } } }).then(checked),
  detail: (id: string) => apiClient.GET("/api/v1/absences/{id}", { params: { path: { id } } }).then(checked),
  record: (body: components["schemas"]["RecordAbsenceCommand"]) =>
    apiClient.POST("/api/v1/absences", { body }).then(checkAbsence),
  edit: (id: string, body: components["schemas"]["AbsenceEditRequest"]) =>
    apiClient.PUT("/api/v1/absences/{id}", { params: { path: { id } }, body }).then(checkAbsence),
  decide: (id: string, body: components["schemas"]["AbsenceDecisionRequest"]) =>
    apiClient.POST("/api/v1/absences/{id}/decide", { params: { path: { id } }, body }).then(checkAbsence),
  cancel: (id: string, reason: string) =>
    apiClient
      .POST("/api/v1/absences/{id}/cancel", { params: { path: { id } }, body: { reason } })
      .then(checkAbsence),
};
export const dayCounting = "Day counting: Mon\u2013Fri without holidays (to confirm with payroll)";
