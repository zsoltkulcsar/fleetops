import { AuthRequestError } from "../auth/api";

export type ReadinessCause = { code: string; documentType: string | null };
export type VehicleReadiness = {
  shownStatus: "active" | "off_road" | "in_maintenance" | "retired";
  usable: boolean;
  causes: ReadinessCause[];
};
export type ReadinessSummary = {
  vehiclesUsable: number;
  vehiclesTotal: number;
  driversAbleToDrive: number;
  driversTotal: number;
};
export type DriverReadinessItem = {
  id: string;
  name: string;
  canDrive: boolean;
  enrolled?: boolean;
  causes: ReadinessCause[];
};
export type DriverReadinessList = { items: DriverReadinessItem[]; nextCursor: string | null };

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
