import { apiClient } from "@fleetops/api-client";
import "./sessionEvents";

export const driverApi = {
  enrol: async (personnelNumber: string, code: string) => {
    const result = await apiClient.POST("/api/v1/auth/driver/enrol", { body: { personnelNumber, code } });
    if (!result.response.ok || !result.data) throw new Error("enrol_failed");
    return result.data;
  },
  config: async () => {
    const result = await apiClient.GET("/api/v1/driver/config", { cache: "no-store" });
    if (!result.response.ok || !result.data) throw new Error("config_failed");
    return result.data;
  },
  privacy: async () => {
    const result = await apiClient.POST("/api/v1/auth/privacy-notice", { body: { version: "1" } });
    if (!result.response.ok) throw new Error("privacy_failed");
  },
  session: async () => {
    const result = await apiClient.GET("/api/v1/auth/session", { cache: "no-store" });
    return result.response.status;
  },
  completePinReset: async () => {
    const result = await apiClient.POST("/api/v1/driver/pin-reset/complete");
    if (!result.response.ok) throw new Error("pin_reset_failed");
  },
  signOut: async () => {
    const result = await apiClient.POST("/api/v1/auth/sign-out");
    if (!result.response.ok && result.response.status !== 401) throw new Error("sign_out_failed");
  },
};
