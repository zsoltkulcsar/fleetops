import { apiClient } from "@fleetops/api-client";

let revoked = false;
function notify(code: unknown) {
  if (code === "session_revoked" && !revoked) {
    revoked = true;
    window.dispatchEvent(new Event("fleetops:driver-revoked"));
  } else if (code === "account_suspended") {
    window.dispatchEvent(new Event("fleetops:driver-suspended"));
  }
}
export function resetSessionState() {
  revoked = false;
}

apiClient.use({
  onRequest({ request }) {
    if (revoked && !new URL(request.url).pathname.startsWith("/api/v1/auth/driver/enrol")) {
      return new Response(JSON.stringify({ code: "session_revoked" }), {
        status: 401,
        headers: { "Content-Type": "application/json" },
      });
    }
  },
  async onResponse({ response, request }) {
    if (!response.ok || new URL(request.url).pathname === "/api/v1/driver/commands/batch") {
      let problem: unknown;
      try {
        problem = await response.clone().json();
      } catch {
        return;
      }
      const code: unknown = problem && typeof problem === "object" ? Reflect.get(problem, "code") : undefined;
      notify(code);
      const results: unknown =
        problem && typeof problem === "object" ? Reflect.get(problem, "results") : undefined;
      if (Array.isArray(results)) {
        for (const result of results) {
          if (result && typeof result === "object") notify(Reflect.get(result, "code"));
        }
      }
    }
  },
});
