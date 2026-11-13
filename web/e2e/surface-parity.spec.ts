import { test, expect } from "./fixtures";
import type { components } from "@fleetops/api-client";

test("fleet and verification queue contain their content at office breakpoints", async ({
  page,
}, testInfo) => {
  const vehicles: components["schemas"]["VehicleListResult"] = {
    nextCursor: null,
    items: [
      {
        id: "vehicle-1",
        kind: "truck",
        plate: "KS-DC 1177",
        make: "Mercedes",
        model: "Actros 1845",
        year: 2024,
        status: "active",
        ownership: "owned",
        version: 1,
        shownStatus: "active",
        usable: true,
        causes: [],
      },
    ],
  };
  const documentRow: components["schemas"]["DocumentListItem"] = {
    id: "document-1",
    ownerType: "vehicle",
    ownerId: "vehicle-1",
    ownerName: "KS-DC 1177",
    type: "registration",
    number: "DOC-1",
    issuedAt: null,
    expiresAt: null,
    status: "uploaded",
    uploadedAt: "2026-11-06T10:00:00Z",
    version: 1,
    canAct: true,
  };
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({ json: { employeeId: "test", roles: ["hr"] } }),
  );
  await page.route("**/api/v1/fleet/vehicles?*", (route) => route.fulfill({ json: vehicles }));
  await page.route("**/api/v1/documents?*", (route) =>
    route.fulfill({ json: { items: [documentRow], nextCursor: null } }),
  );
  await page.route("**/api/v1/documents/counts", (route) =>
    route.fulfill({ json: { queue: 1, expiring: 0, expired: 0, missing: 0 } }),
  );
  await page.route("**/api/v1/notifications?*", (route) => route.fulfill({ json: { unread: 0, items: [] } }));
  for (const colorScheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme });
    await page.goto("/fleet");
    await expect(page.locator("tbody tr")).toHaveCount(1);
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: testInfo.outputPath(`fleet-${colorScheme}.png`), fullPage: true });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    // Round 7 reference (decisions.md, 2026-11-13): two-line vehicle cells (plate, then make and year),
    // like the People rows; this replaces the earlier 47px single-line density.
    const rowBounds = await page.locator("tbody tr").boundingBox();
    expect(rowBounds).not.toBeNull();
    expect(rowBounds!.height).toBeGreaterThanOrEqual(46);
    expect(rowBounds!.height).toBeLessThanOrEqual(64);
    await page.goto("/documents?filter=queue");
    await expect(page.locator(".queue-card")).toHaveCount(1);
    await page.screenshot({ path: testInfo.outputPath(`documents-${colorScheme}.png`), fullPage: true });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole("button", { name: "Verify", exact: true }).scrollIntoViewIfNeeded();
    await expect(page.getByRole("button", { name: "Verify", exact: true })).toBeInViewport();
  }
});
