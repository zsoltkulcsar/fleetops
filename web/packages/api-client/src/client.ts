import createClient from "openapi-fetch";
import type { paths } from "./generated";

export function createApiClient(baseUrl = "/api") {
  return createClient<paths>({ baseUrl });
}

export const apiClient = createApiClient();
export type { paths } from "./generated";
