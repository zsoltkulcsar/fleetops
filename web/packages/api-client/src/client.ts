import createClient from "openapi-fetch";
import type { paths } from "./generated";

export function createApiClient(baseUrl = "") {
  return createClient<paths>({
    baseUrl,
    headers: { "X-FleetOps-Request": "1" },
  });
}

export const apiClient = createApiClient();
export type { paths } from "./generated";
