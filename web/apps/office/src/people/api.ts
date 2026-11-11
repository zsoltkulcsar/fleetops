import { apiClient, type components, type paths } from "@fleetops/api-client";

export type Person = components["schemas"]["PersonDetail"];
export type PersonRow = components["schemas"]["PersonRow"];
export type PersonFields = components["schemas"]["PersonFields"];
export type PersonInput = components["schemas"]["CreatePersonRequest"];
export type PeopleQuery = NonNullable<paths["/api/v1/people"]["get"]["parameters"]["query"]>;

export class PeopleError extends Error {
  constructor(
    public status: number,
    public code: string,
    public types: string[],
    public fields: string[],
  ) {
    super(code);
  }
}

export async function checked<T>(result: { data?: T; error?: unknown; response: Response }): Promise<T> {
  if (!result.response.ok || result.data === undefined) {
    const problem: unknown = result.error;
    const value = (key: string): unknown =>
      problem && typeof problem === "object" ? Reflect.get(problem, key) : undefined;
    const strings = (key: string): string[] => {
      const entries: unknown = value(key);
      return Array.isArray(entries)
        ? entries.filter((entry): entry is string => typeof entry === "string")
        : [];
    };
    throw new PeopleError(
      result.response.status,
      typeof value("code") === "string" ? String(value("code")) : "request_failed",
      strings("types"),
      strings("fields"),
    );
  }
  return result.data;
}

export const peopleApi = {
  list: (query: PeopleQuery) => apiClient.GET("/api/v1/people", { params: { query } }).then(checked),
  detail: (id: string) => apiClient.GET("/api/v1/people/{id}", { params: { path: { id } } }).then(checked),
  create: (body: PersonInput) => apiClient.POST("/api/v1/people", { body }).then(checked),
  edit: (id: string, body: components["schemas"]["EditPersonRequest"]) =>
    apiClient.PUT("/api/v1/people/{id}", { params: { path: { id } }, body }).then(checked),
  activate: (id: string, acceptWarning: boolean) =>
    apiClient
      .POST("/api/v1/people/{id}/activate", { params: { path: { id } }, body: { acceptWarning } })
      .then(checked),
  suspend: (id: string, body: components["schemas"]["PersonReasonRequest"]) =>
    apiClient.POST("/api/v1/people/{id}/suspend", { params: { path: { id } }, body }).then(checked),
  reactivate: (id: string, body: components["schemas"]["PersonReasonRequest"]) =>
    apiClient.POST("/api/v1/people/{id}/reactivate", { params: { path: { id } }, body }).then(checked),
  rehire: (id: string, body: components["schemas"]["PersonReasonRequest"]) =>
    apiClient.POST("/api/v1/people/{id}/rehire", { params: { path: { id } }, body }).then(checked),
  preview: (id: string, endDate: string) =>
    apiClient
      .GET("/api/v1/people/{id}/leave-preview", { params: { path: { id }, query: { endDate } } })
      .then(checked),
  leave: (id: string, body: components["schemas"]["LeavePersonRequest"]) =>
    apiClient.POST("/api/v1/people/{id}/leave", { params: { path: { id } }, body }).then(checked),
  sessions: (id: string) =>
    apiClient.GET("/api/v1/people/{id}/sessions", { params: { path: { id } } }).then(checked),
  revoke: (id: string, sessionId: string, body: components["schemas"]["ReasonRequest"]) =>
    apiClient
      .POST("/api/v1/people/{id}/sessions/{sessionId}/revoke", { params: { path: { id, sessionId } }, body })
      .then(checked),
  resetPin: (id: string) =>
    apiClient.POST("/api/v1/people/{id}/pin-reset", { params: { path: { id } } }).then(checked),
  phone: (id: string, body: components["schemas"]["PhoneRequest"]) =>
    apiClient.PUT("/api/v1/people/{id}/phone", { params: { path: { id } }, body }).then(checked),
};
