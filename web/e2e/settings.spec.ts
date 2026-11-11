import { test, expect } from "./fixtures";
import type { components } from "@fleetops/api-client";
import { mockPeople, officeSession, personId, personRow, peopleProblem } from "./people-helpers";

const settings: components["schemas"]["SettingsView"] = {
  values: {
    "company.max_shift_hours": 12,
    "alerts.escalation_minutes": 60,
    "alerts.a03.blocked_hours": 24,
    "alerts.a03.queue_days": 7,
    "alerts.a08.days": 7,
    "vehicles.lease_lead_days": 30,
    "documents.lead_days.medical": 30,
    "company.payroll_cutoff_day": 15,
  },
  releasableDocTypes: [],
  holidays: [{ date: "2026-12-25", name: "Christmas" }],
};

test.beforeEach(async ({ page }) => {
  await officeSession(page, ["owner"]);
  await mockPeople(page, []);
  await page.route("**/api/v1/settings", (route) => route.fulfill({ json: settings }));
  const jobs: components["schemas"]["JobListResult"] = { items: [] };
  await page.route("**/api/v1/admin/jobs", (route) => route.fulfill({ json: jobs }));
});

test("settings saves lead days and shows the saved value", async ({ page }) => {
  let saved = structuredClone(settings);
  await page.route("**/api/v1/settings", (route) => {
    if (route.request().method() === "PUT") {
      const body: components["schemas"]["UpdateSettingsRequest"] = route.request().postDataJSON();
      expect(body).toEqual({ "documents.lead_days.medical": 45 });
      saved = { ...saved, values: { ...saved.values, "documents.lead_days.medical": 45 } };
    }
    return route.fulfill({ json: saved });
  });
  await page.goto("/settings");
  const input = page.locator('[id="documents.lead_days.medical"]');
  await expect(input).toHaveValue("30");
  await input.fill("45");
  await page.getByRole("button", { name: "Save settings", exact: true }).click();
  await expect(page.getByText("Settings saved.", { exact: true })).toBeVisible();
  await expect(input).toHaveValue("45");
  await page.reload();
  await expect(input).toHaveValue("45");
});

test("hr sees only payroll and holidays as editable", async ({ page }) => {
  await officeSession(page, ["hr"]);
  await page.goto("/settings");
  await expect(page.getByRole("heading", { name: "Payroll", exact: true })).toBeVisible();
  await expect(page.getByLabel("Cutoff day", { exact: true })).toBeEditable();
  await expect(page.getByLabel("Date", { exact: true })).toBeEditable();
  await expect(page.getByLabel("Name", { exact: true })).toBeEditable();
  await expect(page.getByText("Check against your Bundesland (unverified)", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Add holiday", exact: true })).toBeEnabled();
  for (const label of ["Company", "Alerts", "Lead days per document type"]) {
    await expect(page.getByRole("heading", { name: label, exact: true })).toHaveCount(0);
  }
  await expect(page.getByLabel("Maximum shift hours")).toHaveCount(0);
  await expect(page.getByRole("group", { name: "Releasable document types" })).toHaveCount(0);
  await expect(page.locator('input[type="number"]')).toHaveCount(1);
});

test("role change asks for the password and retries", async ({ page }) => {
  await mockPeople(page, [{ ...personRow, roles: ["dispatcher"] }]);
  const attempts: components["schemas"]["RolesRequest"][] = [];
  let verified = false;
  const result: components["schemas"]["AccessActionResult"] = { code: "roles_saved" };
  await page.route(`**/api/v1/people/${personId}/roles`, (route) => {
    attempts.push(route.request().postDataJSON());
    return route.fulfill(
      verified ? { json: result } : { status: 403, json: peopleProblem("step_up_required") },
    );
  });
  await page.route("**/api/v1/auth/step-up", (route) => {
    const body: components["schemas"]["StepUpRequest"] = route.request().postDataJSON();
    expect(body.password).toBe("office-test-password");
    expect(body.code).toBeUndefined();
    verified = true;
    return route.fulfill({ status: 200 });
  });
  await page.goto("/settings");
  await page.getByRole("button", { name: "Edit roles", exact: true }).click();
  await page.getByRole("checkbox", { name: "HR", exact: true }).check();
  await page.getByLabel("Reason", { exact: true }).fill("Payroll responsibility");
  await page.getByRole("button", { name: "Save roles", exact: true }).click();
  await expect(page.getByText("Verify your identity to save roles.", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Password", { exact: true })).toHaveAttribute("type", "password");
  expect(attempts).toHaveLength(1);
  await page.getByLabel("Password", { exact: true }).fill("office-test-password");
  await page.getByRole("button", { name: "Save roles", exact: true }).click();
  await expect(page.getByText("Roles saved.", { exact: true })).toBeVisible();
  expect(attempts).toEqual([
    { roles: ["dispatcher", "hr"], reason: "Payroll responsibility" },
    { roles: ["dispatcher", "hr"], reason: "Payroll responsibility" },
  ]);
});

function releaseVehicle(): components["schemas"]["VehicleDetail"] {
  return {
    id: personId,
    kind: "truck",
    plate: "B-X 1001",
    make: "Mercedes",
    model: "Actros",
    year: 2024,
    status: "active",
    ownership: "owned",
    version: 1,
    shownStatus: "off_road",
    usable: false,
    causes: [{ code: "document_expired", documentType: "hu_report" }],
    vin: "WDB00000000000001",
    grossWeightKg: 40000,
    requiredLicenceClass: "C+E",
    leaseEndDate: null,
    trailerType: null,
    odometerKm: 120000,
    depotId: personId,
    retiredReason: null,
    retiredAt: null,
    serviceIntervalKm: 60000,
    serviceIntervalMonths: 12,
    lastTachoDownloadAt: null,
    plateHistory: [],
    requiredDocuments: [],
    release: {
      enabled: false,
      allowed: false,
      expiredTypes: ["hu_report"],
      releasableTypes: [],
      activeOverride: null,
    },
  };
}

test("release is hidden when no type is enabled", async ({ page }) => {
  const vehicle = releaseVehicle();
  const list: components["schemas"]["VehicleListResult"] = { items: [vehicle], nextCursor: null };
  await page.route("**/api/v1/fleet/vehicles?*", (route) => route.fulfill({ json: list }));
  await page.route(`**/api/v1/fleet/vehicles/${personId}`, (route) => route.fulfill({ json: vehicle }));
  await page.goto("/fleet");
  const row = page.getByRole("row").filter({ hasText: "B-X 1001" });
  await expect(row.getByText("B-X 1001", { exact: true })).toBeVisible();
  await expect(row.getByRole("button", { name: "Release", exact: true })).toHaveCount(0);
  await expect(row).not.toContainText("Release blocked by");
});

test("release shows the banner until valid until", async ({ page }) => {
  // The clock runs from 12:00 so the page can load; the override ends at 12:05.
  await page.clock.install({ time: new Date("2026-11-11T12:00:00Z") });
  const vehicle = releaseVehicle();
  vehicle.release = {
    enabled: true,
    allowed: false,
    expiredTypes: ["hu_report"],
    releasableTypes: ["hu_report"],
    activeOverride: { byName: "Olivia Owner", validUntil: "2026-11-11T12:05:00Z" },
  };
  const list: components["schemas"]["VehicleListResult"] = { items: [vehicle], nextCursor: null };
  await page.route("**/api/v1/fleet/vehicles?*", (route) => route.fulfill({ json: list }));
  await page.route(`**/api/v1/fleet/vehicles/${personId}`, (route) => route.fulfill({ json: vehicle }));
  await page.goto("/fleet");
  const banner = page.getByText("Released by Olivia Owner until 11.11.", { exact: true });
  await expect(banner).toBeVisible();
  await page.clock.runFor(3 * 60_000);
  await expect(banner).toBeVisible();
  await page.clock.runFor(3 * 60_000);
  await expect(banner).toHaveCount(0);
  await expect(page.getByText("B-X 1001", { exact: true })).toBeVisible();
});
