import { AuthRequestError } from "../auth/api";

export type Vehicle = {
  id: string;
  kind: string | number;
  plate: string;
  make: string;
  model: string;
  year: number;
  status: string;
  ownership: string | number;
  version: number;
};
export type VehicleDetail = Vehicle & {
  vin?: string | null;
  grossWeightKg?: number | null;
  requiredLicenceClass?: string | null;
  leaseEndDate?: string | null;
  trailerType?: string | number | null;
  odometerKm?: number | null;
  depotId?: string | null;
  serviceIntervalKm?: number | null;
  serviceIntervalMonths?: number | null;
  lastTachoDownloadAt?: string | null;
  requiredDocuments?: Array<{ name: string; status: string; expiryDate?: string | null }>;
  plateHistory?: Array<{ plate: string; from?: string | null; to?: string | null }>;
};
export type VehicleList = { items: Vehicle[]; nextCursor?: string | null };
export type VehicleInput = Record<string, unknown>;
export type ApiProblem = { code?: string; errors?: Record<string, string[] | string>; detail?: string };

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
  list: (params: { cursor?: string; limit?: number; search?: string; kind?: string }) => {
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
  update: (id: string, input: VehicleInput) =>
    request<VehicleDetail>(`/api/v1/fleet/vehicles/${encodeURIComponent(id)}`, {
      method: "PUT",
      body: JSON.stringify(input),
    }),
};
