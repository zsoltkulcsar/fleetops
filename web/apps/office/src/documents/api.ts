import { AuthRequestError } from "../auth/api";
import type { components } from "@fleetops/api-client";

export type Document = components["schemas"]["DocumentListItem"];
export type DocumentList = components["schemas"]["DocumentListResult"];
export type DocumentType = components["schemas"]["DocumentType"];
export type DocumentOwnerType = components["schemas"]["DocumentOwnerType"];
export type DocumentStatus = components["schemas"]["DocumentStatus"];
export type DocumentCreateRequest = components["schemas"]["DocumentCreateRequest"];
export type UploadLinkResult = components["schemas"]["UploadLinkResult"];
export type PeopleLookupResult = components["schemas"]["PeopleLookupResult"];
export type VehicleListResult = components["schemas"]["VehicleListResult"];

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

export const documentTypes: Record<DocumentOwnerType, readonly DocumentType[]> = {
  employee: [
    "licence_c_ce",
    "driver_card",
    "code95",
    "medical",
    "adr_certificate",
    "id_card",
    "employment_contract",
  ],
  vehicle: ["registration", "hu_report", "sp_report", "tacho_calibration", "insurance", "community_licence"],
};

export const documentsApi = {
  list: (params: { cursor?: string; search?: string }) => {
    const query = new URLSearchParams();
    query.set("cursor", params.cursor ?? "");
    query.set("limit", "50");
    query.set("search", params.search ?? "");
    return request<DocumentList>(`/api/v1/documents?${query.toString()}`);
  },
  people: (search: string) => {
    const query = new URLSearchParams({ search });
    return request<PeopleLookupResult>(`/api/v1/people/lookup?${query.toString()}`);
  },
  vehicles: (search: string) => {
    const query = new URLSearchParams({ search, limit: "50", includeRetired: "false" });
    return request<VehicleListResult>(`/api/v1/fleet/vehicles?${query.toString()}`);
  },
  uploadLink: (input: components["schemas"]["DocumentUploadRequest"]) =>
    request<UploadLinkResult>("/api/v1/documents/upload-links", {
      method: "POST",
      body: JSON.stringify(input),
    }),
  create: (input: DocumentCreateRequest) =>
    request<components["schemas"]["DocumentDetail"]>("/api/v1/documents", {
      method: "POST",
      body: JSON.stringify(input),
    }),
};
