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
export type DocumentDetail = components["schemas"]["DocumentDetail"];
export type DocumentCounts = components["schemas"]["DocumentCounts"];
export type DocumentMissing = components["schemas"]["DocumentMissingResult"];
export type DocumentSearchResult = components["schemas"]["DocumentSearchResult"];

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

/** Types whose expiry date must be set when the document is verified. */
export const typesWithExpiry: readonly DocumentType[] = [
  "licence_c_ce",
  "driver_card",
  "code95",
  "medical",
  "adr_certificate",
  "id_card",
  "hu_report",
  "sp_report",
  "tacho_calibration",
  "insurance",
  "community_licence",
];

export const documentsApi = {
  list: (params: { cursor?: string; search?: string; filter?: string }) => {
    const query = new URLSearchParams();
    query.set("cursor", params.cursor ?? "");
    query.set("limit", "50");
    query.set("search", params.search ?? "");
    query.set("filter", params.filter ?? "");
    return request<DocumentList>(`/api/v1/documents?${query.toString()}`);
  },
  people: (search: string, id?: string) => {
    const query = new URLSearchParams({ search });
    if (id) query.set("id", id);
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
  counts: () => request<DocumentCounts>("/api/v1/documents/counts"),
  missing: (cursor?: string) =>
    request<DocumentMissing>(`/api/v1/documents/missing?cursor=${encodeURIComponent(cursor ?? "")}&limit=50`),
  detail: (id: string) => request<DocumentDetail>(`/api/v1/documents/${id}`),
  fileLink: (id: string, fileId: string) =>
    request<{ url: string; expiresAt: string }>(`/api/v1/documents/${id}/files/${fileId}/link`),
  verify: (id: string, input: components["schemas"]["DocumentVerifyRequest"]) =>
    request<DocumentDetail>(`/api/v1/documents/${id}/verify`, {
      method: "POST",
      body: JSON.stringify(input),
    }),
  reject: (id: string, input: components["schemas"]["DocumentRejectRequest"]) =>
    request<DocumentDetail>(`/api/v1/documents/${id}/reject`, {
      method: "POST",
      body: JSON.stringify(input),
    }),
  remove: (id: string, input: components["schemas"]["DocumentDeleteRequest"]) =>
    request<DocumentDetail>(`/api/v1/documents/${id}/delete`, {
      method: "POST",
      body: JSON.stringify(input),
    }),
  sightCheck: (id: string, input: components["schemas"]["DocumentSightCheckRequest"]) =>
    request<DocumentDetail>(`/api/v1/documents/${id}/sight-check`, {
      method: "POST",
      body: JSON.stringify(input),
    }),
  searchAll: (q: string) => request<DocumentSearchResult>(`/api/v1/search?q=${encodeURIComponent(q)}`),
};
