import { test as base } from "@playwright/test";

export const test = base.extend({
  page: async ({ page }, use) => {
    await page.route("**/api/v1/auth/session", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ employeeId: "demo", roles: ["owner"] }),
      }),
    );
    await page.route("**/api/v1/fleet/vehicles?*", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ items: [], nextCursor: null }),
      }),
    );
    await use(page);
  },
});
export { expect } from "@playwright/test";
