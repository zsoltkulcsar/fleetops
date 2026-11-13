import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { components } from "@fleetops/api-client";

type Schema = components["schemas"];
const truckA = "00000000-0000-0000-0000-00000000000a";
const truckB = "00000000-0000-0000-0000-00000000000b";
const trailer = "00000000-0000-0000-0000-00000000000c";

function today() {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Berlin" }).format(new Date());
}

const vehicle = (id: string, plate: string, overrides: Partial<Schema["VehicleListItem"]> = {}) =>
  ({
    id,
    kind: "truck",
    plate,
    make: "MAN",
    model: "TGX",
    year: 2023,
    status: "active",
    ownership: "owned",
    version: 1,
    shownStatus: "active",
    usable: true,
    causes: [],
    ...overrides,
  }) satisfies Schema["VehicleListItem"];

const vehicles: Schema["VehicleListItem"][] = [
  vehicle(truckA, "B-FO 1001"),
  vehicle(truckB, "B-FO 1002", {
    usable: false,
    causes: [{ code: "document_expired", documentType: "hu_report" }],
  }),
  vehicle(trailer, "B-FO 9001", {
    kind: "trailer",
    make: "Krone",
    model: "Profi Liner",
    ownership: "leased",
  }),
];

const defect = (id: string, severity: Schema["DefectSeverity"]): Schema["DefectRow"] => ({
  id,
  vehicleId: truckB,
  plate: "B-FO 1002",
  category: "brakes",
  severity,
  reportedBy: "11111111-1111-1111-1111-111111111111",
  reportedByName: "Dana Driver",
  reportedAt: "2026-11-10T08:00:00Z",
  photosCount: 1,
  status: "open",
  ageDays: 3,
  origin: "app",
});

const repair: Schema["RepairDetail"] = {
  id: "22222222-2222-2222-2222-222222222222",
  type: "service",
  vehicleId: truckA,
  workshop: "Werkstatt Nord",
  scheduledFor: "2026-11-01",
  defectReportIds: [],
  status: "scheduled",
  cancelReason: null,
  doneAt: null,
  odometerAtDoneKm: null,
  inWorkshop: false,
  overdue: true,
  kmReadingStale: false,
};

async function routes(page: Page) {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({ json: { employeeId: "dispatcher", roles: ["dispatcher"] } }),
  );
  const list: Schema["VehicleListResult"] = { items: vehicles, nextCursor: null };
  await page.route("**/api/v1/fleet/vehicles?*", (route) => route.fulfill({ json: list }));
  const summary: Schema["ReadinessSummary"] = {
    vehiclesUsable: 2,
    vehiclesTotal: 3,
    driversAbleToDrive: 1,
    driversTotal: 1,
  };
  await page.route("**/api/v1/readiness/summary", (route) => route.fulfill({ json: summary }));
  const defects: Schema["DefectListResult"] = {
    items: [defect("d1", "critical"), defect("d2", "minor")],
    page: 1,
    pageSize: 100,
    total: 2,
    openByOrigin: { app: 2, officeFromChat: 0 },
  };
  await page.route("**/api/v1/fleet/defects?*", (route) => route.fulfill({ json: defects }));
  const repairs: Schema["MaintenanceList"] = { items: [repair] };
  await page.route("**/api/v1/fleet/maintenance*", (route) => route.fulfill({ json: repairs }));
  const schedule: Schema["ScheduleResult"] = {
    generatedAt: "2026-11-13T08:00:00Z",
    items: [
      {
        id: "33333333-3333-3333-3333-333333333333",
        employeeId: "11111111-1111-1111-1111-111111111111",
        employeeName: "Dana Driver",
        vehicleId: truckA,
        vehiclePlate: "B-FO 1001",
        from: today(),
        to: today(),
        status: "active",
        source: "manual",
        patternSlot: null,
        hasClash: false,
        version: 1,
      },
    ],
  };
  await page.route("**/api/v1/schedule?*", (route) => route.fulfill({ json: schedule }));
  const detail: Schema["VehicleDetail"] = {
    ...vehicles[1],
    vin: "WMA00000000000002",
    grossWeightKg: 18000,
    ownership: "owned",
    leaseEndDate: null,
    trailerType: null,
    odometerKm: 210000,
    depotId: "44444444-4444-4444-4444-444444444444",
    retiredReason: null,
    retiredAt: null,
    requiredLicenceClass: "C",
    serviceIntervalKm: 50000,
    serviceIntervalMonths: 12,
    lastTachoDownloadAt: null,
    plateHistory: [],
    requiredDocuments: [
      { type: "registration", needsConfirmation: false },
      { type: "hu_report", needsConfirmation: true },
    ],
    shownStatus: "active",
    usable: false,
    causes: [{ code: "document_expired", documentType: "hu_report" }],
  };
  await page.route(`**/api/v1/fleet/vehicles/${truckB}`, (route) => route.fulfill({ json: detail }));
}

test.beforeEach(async ({ page }) => {
  await routes(page);
});

test("cards count ready and unusable vehicles, open defects and planned service", async ({ page }) => {
  await page.goto("/fleet");
  const cards = page.getByRole("region", { name: "Fleet at a glance" });
  await expect(cards).toContainText("Ready to drive");
  await expect(cards).toContainText("2 of 3 usable");
  await expect(cards).toContainText("1 critical");
  await expect(cards).toContainText("1 overdue");
  await expect(cards.getByText("Coming in Epic 15", { exact: true })).toHaveCount(1);
});

test("rows show today's driver, open defects and the next service", async ({ page }) => {
  await page.goto("/fleet");
  const a = page.getByRole("row").filter({ hasText: "B-FO 1001" });
  await expect(a.getByText("Dana Driver", { exact: true })).toBeVisible();
  await expect(a.getByText("Overdue", { exact: true })).toBeVisible();
  const b = page.getByRole("row").filter({ hasText: "B-FO 1002" });
  await expect(b.getByRole("link", { name: "Plan a driver" })).toHaveAttribute("href", "/plan");
  await expect(b.getByText("2 open \u00b7 1 critical")).toBeVisible();
  const t = page.getByRole("row").filter({ hasText: "B-FO 9001" });
  await expect(t.getByRole("cell", { name: "Leased", exact: true })).toBeVisible();
});

test("switcher and pill point to defects and maintenance with counts", async ({ page }) => {
  await page.goto("/fleet");
  const views = page.getByRole("group", { name: "Fleet views" });
  await expect(views.getByRole("link", { name: /^Vehicles 3/ })).toHaveAttribute("aria-current", "page");
  await expect(views.getByRole("link", { name: /Defects 2 open/ })).toHaveAttribute("href", "/fleet/defects");
  await expect(views.getByRole("link", { name: /Maintenance 1 overdue/ })).toHaveAttribute(
    "href",
    "/fleet/maintenance",
  );
  await expect(page.getByRole("link", { name: /1 critical defect needs a decision/ })).toHaveAttribute(
    "href",
    "/fleet/defects",
  );
});

test("status chip filters the loaded vehicles", async ({ page }) => {
  await page.goto("/fleet");
  await page
    .getByRole("group", { name: "Filter by status" })
    .getByRole("button", { name: /^Not usable/ })
    .click();
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await expect(page.getByRole("row").filter({ hasText: "B-FO 1002" })).toBeVisible();
  await page.getByRole("button", { name: "Clear filters" }).click();
  await expect(page.locator("tbody tr")).toHaveCount(3);
});

test("drawer shows why a vehicle cannot drive, its papers, defects and placeholders", async ({ page }) => {
  await page.goto("/fleet");
  await page.getByRole("button", { name: "Quick view of B-FO 1002" }).click();
  const dialog = page.getByRole("dialog", { name: "B-FO 1002" });
  await expect(dialog.getByText("WMA00000000000002", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Why it cannot be used")).toBeVisible();
  await dialog.getByRole("tab", { name: /Documents/ }).click();
  const hu = dialog.getByRole("listitem").filter({ hasText: "HU report" });
  await expect(hu.getByText("Expired", { exact: true })).toBeVisible();
  await dialog.getByRole("tab", { name: /Defects/ }).click();
  await expect(dialog.getByRole("tabpanel")).toContainText("Reported by Dana Driver");
  await dialog.getByRole("tab", { name: /Costs/ }).click();
  await expect(dialog.getByRole("tabpanel").getByText("Coming in Epic 15", { exact: true })).toHaveCount(4);
  await expect(dialog.getByRole("link", { name: "Open full record" })).toHaveAttribute(
    "href",
    `/fleet/${truckB}`,
  );
});

test("fleet page and drawer have no accessibility violations", async ({ page }) => {
  await page.goto("/fleet");
  await expect(page.getByRole("row").filter({ hasText: "B-FO 1001" })).toBeVisible();
  const tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];
  const list = await new AxeBuilder({ page: page as never }).withTags(tags).analyze();
  expect(list.violations).toEqual([]);
  await page.getByRole("button", { name: "Quick view of B-FO 1002" }).click();
  await expect(page.getByRole("dialog", { name: "B-FO 1002" }).getByText("WMA00000000000002")).toBeVisible();
  const drawer = await new AxeBuilder({ page: page as never }).withTags(tags).analyze();
  expect(drawer.violations).toEqual([]);
});
