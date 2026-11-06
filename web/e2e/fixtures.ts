import { test as base } from "@playwright/test";
import type { components } from "@fleetops/api-client";

export const test = base.extend({
  page: async ({ page }, use) => {
    await page.route("**/api/v1/auth/session", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ employeeId: "demo", roles: ["owner"] }),
      }),
    );
    const emptyList: components["schemas"]["VehicleListResult"] = { items: [], nextCursor: null };
    await page.route("**/api/v1/fleet/vehicles?*", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(emptyList),
      }),
    );
    await use(page);
  },
});
export { expect } from "@playwright/test";
