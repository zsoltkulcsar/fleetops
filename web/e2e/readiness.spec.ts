import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { components } from "@fleetops/api-client";

type Vehicle = components["schemas"]["VehicleListItem"] & {
  shownStatus: "active" | "off_road" | "in_maintenance" | "retired";
  usable: boolean;
  causes: Array<{ code: string; documentType: string | null }>;
};
type VehicleDetail = components["schemas"]["VehicleDetail"] & {
  shownStatus: Vehicle["shownStatus"];
  usable: boolean;
  causes: Vehicle["causes"];
};
type Summary = {
  vehiclesUsable: number;
  vehiclesTotal: number;
  driversAbleToDrive: number;
  driversTotal: number;
};
type Driver = { id: string; name: string; canDrive: boolean; causes: Vehicle["causes"] };

const vehicle = (overrides: Partial<Vehicle> = {}): Vehicle => ({
  id: "v-readiness",
  kind: "truck",
  plate: "B-RO 2300",
  make: "MAN",
  model: "TGX",
  year: 2024,
  status: "active",
  ownership: "owned",
  version: 1,
  shownStatus: "active",
  usable: true,
  causes: [],
  ...overrides,
});

const detail = (overrides: Partial<VehicleDetail> = {}): VehicleDetail => ({
  ...vehicle(),
  vin: "WVWZZZ1JZXW123456",
  grossWeightKg: 8000,
  requiredLicenceClass: "C",
  leaseEndDate: null,
  trailerType: null,
  odometerKm: 1000,
  depotId: "11111111-1111-1111-1111-111111111111",
  retiredReason: null,
  retiredAt: null,
  serviceIntervalKm: 50000,
  serviceIntervalMonths: 12,
  lastTachoDownloadAt: null,
  plateHistory: [],
  requiredDocuments: [],
  ...overrides,
});

async function readinessRoutes(page: Page, items: Vehicle[] = [vehicle()]) {
  await page.route("**/api/v1/fleet/vehicles?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items, nextCursor: null }),
    }),
  );
  await page.route("**/api/v1/readiness/summary", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        vehiclesUsable: 3,
        vehiclesTotal: 5,
        driversAbleToDrive: 2,
        driversTotal: 3,
      } satisfies Summary),
    }),
  );
}

test("fleet table shows off road with icon and label", async ({ page }) => {
  await readinessRoutes(page, [
    vehicle({
      shownStatus: "off_road",
      usable: false,
      causes: [{ code: "document_expired", documentType: "hu_report" }],
    }),
  ]);
  await page.goto("/fleet");
  await expect(page.getByText("Off road")).toBeVisible();
  await expect(page.getByRole("row").last()).toContainText("B-RO 2300");
});

test("not usable shows its cause", async ({ page }) => {
  await page.route("**/api/v1/fleet/vehicles/v-readiness", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(
        detail({
          usable: false,
          shownStatus: "off_road",
          causes: [{ code: "document_expired", documentType: "hu_report" }],
        }),
      ),
    }),
  );
  await page.goto("/fleet/v-readiness");
  await expect(page.getByText("HU report expired", { exact: true })).toBeVisible();
});

test("fleet page shows the usable count", async ({ page }) => {
  await readinessRoutes(page);
  await page.goto("/fleet");
  await expect(page.getByText("3 of 5 usable", { exact: true })).toBeVisible();
});

test("record shows set off road for a dispatcher", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "dispatcher", roles: ["dispatcher"] }),
    }),
  );
  await page.route("**/api/v1/fleet/vehicles/v-readiness", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(detail()) }),
  );
  await page.goto("/fleet/v-readiness");
  await expect(page.getByRole("button", { name: /set off road/i })).toBeVisible();
});

test("people page shows the masked block text", async ({ page }) => {
  const driver: Driver = {
    id: "d-1",
    name: "A Driver",
    canDrive: false,
    causes: [{ code: "document_expired", documentType: null }],
  };
  await page.route("**/api/v1/readiness/drivers?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [driver], nextCursor: null }),
    }),
  );
  await page.goto("/people");
  await expect(page.getByText("Blocked — document expired", { exact: true })).toBeVisible();
  await expect(page.getByText("Medical", { exact: true })).not.toBeVisible();
});

test("people page shows the type to hr", async ({ page }) => {
  const driver: Driver = {
    id: "d-1",
    name: "A Driver",
    canDrive: false,
    causes: [{ code: "document_expired", documentType: "medical" }],
  };
  await page.route("**/api/v1/readiness/drivers?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [driver], nextCursor: null }),
    }),
  );
  await page.goto("/people");
  await expect(page.getByText("Blocked — Medical expired", { exact: true })).toBeVisible();
});

test("people page offers blocked only", async ({ page }) => {
  await page.route("**/api/v1/readiness/drivers?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], nextCursor: null }),
    }),
  );
  await page.goto("/people");
  await expect(page.getByRole("checkbox", { name: /blocked/i })).toBeVisible();
});

test("documents missing filter opens a prefilled upload", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "dispatcher", roles: ["dispatcher"] }),
    }),
  );
  await page.route("**/api/v1/documents?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], nextCursor: null }),
    }),
  );
  await page.route("**/api/v1/documents/counts", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ queue: 0, expiring: 0, expired: 0, missing: 0 }),
    }),
  );
  await page.route("**/api/v1/documents/missing?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], nextCursor: null }),
    }),
  );
  await page.goto("/documents?ownerId=v-readiness");
  await expect(page.getByRole("dialog")).toBeVisible();
});

test("filter missing alone opens no dialog", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "dispatcher", roles: ["dispatcher"] }),
    }),
  );
  await page.route("**/api/v1/documents/counts", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ queue: 0, expiring: 0, expired: 0, missing: 0 }),
    }),
  );
  await page.route("**/api/v1/documents/missing?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], nextCursor: null }),
    }),
  );
  await page.goto("/documents?filter=missing");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("people page has no accessibility violations", async ({ page }) => {
  await page.goto("/people");
  const result = await new AxeBuilder({ page: page as never })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
    .analyze();
  expect(result.violations).toEqual([]);
});
