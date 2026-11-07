import { AuthRequestError } from "../auth/api";
import type { components } from "@fleetops/api-client";

export type Attention = components["schemas"]["AttentionResult"];
export type AttentionGroup = components["schemas"]["AttentionGroup"];
export type AttentionItem = components["schemas"]["AttentionItem"];

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { "Content-Type": "application/json", ...(init?.method ? { "X-FleetOps-Request": "1" } : {}) },
  });
  if (!response.ok) {
    let problem: { code?: string; fields?: string[] } = {};
    try {
      problem = (await response.json()) as typeof problem;
    } catch {
      /* empty */
    }
    if (response.status === 401) window.dispatchEvent(new CustomEvent("fleetops:auth-expired"));
    throw new AuthRequestError(response.status, problem);
  }
  return (await response.json()) as T;
}

export const attentionApi = {
  list: () => request<Attention>("/api/v1/attention"),
  snooze: (id: string, input: { version: number; until: string; reason: string }) =>
    request(`/api/v1/alerts/${id}/snooze`, { method: "POST", body: JSON.stringify(input) }),
  unsnooze: (id: string, version: number) =>
    request(`/api/v1/alerts/${id}/unsnooze`, { method: "POST", body: JSON.stringify({ version }) }),
};
