import { AuthRequestError } from "../auth/api";

import type { components } from "@fleetops/api-client";

export type ReadinessCause = components["schemas"]["ReadinessCause"];
export type VehicleReadiness = Pick<
  components["schemas"]["VehicleDetail"],
  "shownStatus" | "usable" | "causes"
>;
export type ReadinessSummary = components["schemas"]["ReadinessSummary"];
export type DriverReadinessItem = components["schemas"]["DriverReadinessItem"];
export type DriverReadinessList = components["schemas"]["DriverReadinessList"];

async function request<T>(path: string): Promise<T> {
  const response = await fetch(path);
  if (!response.ok) {
    throw new AuthRequestError(response.status, {});
  }
  return (await response.json()) as T;
}

export const readinessApi = {
  summary: () => request<ReadinessSummary>("/api/v1/readiness/summary"),
  drivers: (blockedOnly: boolean) =>
    request<DriverReadinessList>(`/api/v1/readiness/drivers?limit=50&blockedOnly=${String(blockedOnly)}`),
};
