import { AuthRequestError } from "../auth/api";
import type { components } from "@fleetops/api-client";

export type Vehicle = components["schemas"]["VehicleListItem"];
export type VehicleDetail = components["schemas"]["VehicleDetail"];
export type VehicleList = components["schemas"]["VehicleListResult"];
export type VehicleInput = components["schemas"]["CreateVehicleCommand"];
export type VehicleEditInput = components["schemas"]["EditVehicleRequest"];
export type ApiProblem = {
  code?: string;
  errors?: Record<string, string[] | string>;
  fields?: string[];
  detail?: string;
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
      ...(init?.method && init.method !== "GET" ? { "X-FleetOps-Request": "1" } : {}),
    },
  });
  if (!response.ok) {
    let problem: ApiProblem = {};
    try {
      problem = (await response.json()) as ApiProblem;
    } catch {
      problem = {};
    }
    if (response.status === 401) window.dispatchEvent(new CustomEvent("fleetops:auth-expired"));
    throw new AuthRequestError(response.status, problem);
  }
  return (await response.json()) as T;
}

export const fleetApi = {
  list: (params: {
    cursor?: string;
    limit?: number;
    search?: string;
    kind?: components["schemas"]["VehicleKind"];
  }) => {
    const query = new URLSearchParams();
    query.set("cursor", params.cursor ?? "");
    query.set("limit", String(params.limit ?? 50));
    query.set("search", params.search ?? "");
    query.set("kind", params.kind ?? "");
    return request<VehicleList>(`/api/v1/fleet/vehicles?${query.toString()}`);
  },
  detail: (id: string) => request<VehicleDetail>(`/api/v1/fleet/vehicles/${encodeURIComponent(id)}`),
  create: (input: VehicleInput) =>
    request<VehicleDetail>("/api/v1/fleet/vehicles", { method: "POST", body: JSON.stringify(input) }),
  update: (id: string, input: VehicleEditInput) =>
    request<VehicleDetail>(`/api/v1/fleet/vehicles/${encodeURIComponent(id)}`, {
      method: "PUT",
      body: JSON.stringify(input),
    }),
};
