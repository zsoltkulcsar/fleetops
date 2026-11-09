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
    const emptyImpact: components["schemas"]["VehicleRetirementImpact"] = { items: [] };
    await page.route("**/api/v1/fleet/vehicles/*/retirement-impact", (route) =>
      route.fulfill({ json: emptyImpact }),
    );
    await page.route("**/api/v1/fleet/vehicles?*", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(emptyList),
      }),
    );
    await page.route("**/api/v1/attention", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          groups: [],
          totals: { open: 0, critical: 0 },
          companyEmpty: false,
        }),
      }),
    );
    await page.route("**/api/v1/readiness/summary", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ vehiclesUsable: 0, vehiclesTotal: 0 }),
      }),
    );
    await use(page);
  },
});
export { expect } from "@playwright/test";
