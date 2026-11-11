export type NextStep = "done" | "second_factor" | "enrol_second_factor" | "privacy_notice";
export type Session = { employeeId: string; roles: string[]; stepUpMethod: "code" | "password" };
export type Problem = {
  code?: string;
  detail?: string;
  errors?: Record<string, string[] | string>;
  fields?: string[];
};

export function installAuthExpiryHandler() {
  const originalFetch = window.fetch.bind(window);
  window.fetch = async (input, init) => {
    const response = await originalFetch(input, init);
    const requestUrl = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    const url = new URL(requestUrl, window.location.origin);
    if (response.status === 401 && !url.pathname.startsWith("/api/v1/auth/")) {
      window.dispatchEvent(new CustomEvent("fleetops:auth-expired"));
    }
    return response;
  };
}

export class AuthRequestError extends Error {
  constructor(
    public status: number,
    public problem: Problem = {},
  ) {
    super(problem.code ?? "auth_request_failed");
  }
}

async function request<T>(path: string, init?: RequestInit, isSession = false): Promise<T> {
  const url = path.startsWith("/api/") ? path : `/api/v1/auth${path}`;
  const response = await fetch(url, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
      ...(init?.method && init.method !== "GET" ? { "X-FleetOps-Request": "1" } : {}),
    },
  });
  if (!response.ok) {
    let problem: Problem = {};
    try {
      problem = (await response.json()) as Problem;
    } catch {
      /* empty response */
    }
    if (response.status === 401 && !isSession && !url.startsWith("/api/v1/auth/")) {
      window.dispatchEvent(new CustomEvent("fleetops:auth-expired"));
    }
    throw new AuthRequestError(response.status, problem);
  }
  if (response.status === 204) return undefined as T;
  const body = await response.text();
  return (body ? JSON.parse(body) : undefined) as T;
}

export const authApi = {
  session: () => request<Session>("/session", undefined, true),
  signIn: (email: string, password: string) =>
    request<{ next: NextStep }>("/sign-in", { method: "POST", body: JSON.stringify({ email, password }) }),
  secondFactor: (code: string) =>
    request<{ next: NextStep }>("/second-factor", { method: "POST", body: JSON.stringify({ code }) }),
  enrol: () => request<{ secret: string; otpauthUri: string }>("/second-factor/enrol", { method: "POST" }),
  privacy: (version: string) =>
    request<{ next: NextStep }>("/privacy-notice", { method: "POST", body: JSON.stringify({ version }) }),
  forgot: (email: string) =>
    request<void>("/forgot-password", { method: "POST", body: JSON.stringify({ email }) }),
  reset: (token: string, password: string) =>
    request<void>("/reset-password", { method: "POST", body: JSON.stringify({ token, password }) }),
  signOut: () => request<void>("/sign-out", { method: "POST" }),
};
