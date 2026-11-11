import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";

type Schema = components["schemas"];
const vehicleId = "00000000-0000-0000-0000-000000000030";
const maintenanceId = "00000000-0000-0000-0000-000000000091";
const maintenance: Schema["RepairDetail"] = {
  id: maintenanceId,
  vehicleId,
  type: "service",
  workshop: "Central workshop",
  scheduledFor: "2026-11-10",
  dueDate: "2026-11-09",
  dueKm: 120000,
  status: "scheduled",
  defectReportIds: [],
  cancelReason: null,
  doneAt: null,
  odometerAtDoneKm: null,
  inWorkshop: false,
  overdue: true,
  odometerKm: 125000,
  kmAsOf: "2026-11-08T10:00:00Z",
  kmReadingStale: false,
};

async function setup(page: Page, rule: "A-06" | "A-12" = "A-06") {
  await page.clock.setFixedTime(new Date("2026-11-10T10:00:00Z"));
  const state = {
    items: [{ ...maintenance }],
    requests: [] as { method: string; path: string; body: unknown }[],
    downloaded: false,
  };
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({ json: { employeeId: vehicleId, roles: ["dispatcher"], stepUpMethod: "password" } }),
  );
  await page.route("**/api/v1/fleet/vehicles?*", (route) =>
    route.fulfill({
      json: {
        items: [
          {
            id: vehicleId,
            kind: "truck",
            plate: "B-TEST 32",
            make: "MAN",
            model: "TGX",
            year: 2024,
            status: "active",
            ownership: "owned",
            version: 1,
            shownStatus: "active",
            usable: true,
            causes: [],
          },
        ],
        nextCursor: null,
      } satisfies Schema["VehicleListResult"],
    }),
  );
  await page.route("**/api/v1/fleet/maintenance**", (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === "GET")
      return route.fulfill({ json: { items: state.items } satisfies Schema["MaintenanceList"] });
    const body: unknown = request.postDataJSON();
    state.requests.push({ method: request.method(), path, body });
    if (path.endsWith("/done")) {
      state.items[0] = {
        ...state.items[0],
        status: "done",
        overdue: false,
        doneAt: "2026-11-10",
        odometerAtDoneKm: 125000,
      };
      return route.fulfill({
        json: {
          ...state.items[0],
          nextService: { vehicleId, type: "service", dueDate: "2027-11-10", dueKm: 175000 },
        } satisfies Schema["RepairDetail"],
      });
    }
    if (path.endsWith("/cancel")) {
      state.items[0] = {
        ...state.items[0],
        status: "cancelled",
        cancelReason: (body as Schema["CancelRepairRequest"]).reason,
      };
    } else if (request.method() === "PUT") {
      Object.assign(state.items[0], body);
    } else {
      state.items.push({
        ...maintenance,
        ...(body as Schema["CreateRepairCommand"]),
        id: "00000000-0000-0000-0000-000000000092",
        overdue: false,
      });
    }
    return route.fulfill({ json: state.items.at(-1) });
  });
  await page.route("**/api/v1/attention", (route) =>
    route.fulfill({
      json: {
        groups: state.downloaded
          ? []
          : [
              {
                subjectType: "vehicle",
                subjectId: vehicleId,
                subjectName: "B-TEST 32",
                worstSeverity: "warning",
                items: [
                  {
                    id: maintenanceId,
                    ruleId: rule,
                    severity: "warning",
                    scopeKey: rule === "A-06" ? maintenanceId : "download_due",
                    dueAt: "2026-11-09",
                    status: "open",
                    snoozable: true,
                    version: 1,
                  },
                ],
              },
            ],
        totals: { open: state.downloaded ? 0 : 1, critical: 0 },
        companyEmpty: false,
      } satisfies Schema["AttentionResult"],
    }),
  );
  await page.route("**/api/v1/readiness/summary", (route) =>
    route.fulfill({ json: { vehiclesUsable: 1, vehiclesTotal: 1 } }),
  );
  await page.route(`**/api/v1/fleet/vehicles/${vehicleId}/tacho-downloads`, (route) => {
    const body: Schema["LogTachoDownloadRequest"] = route.request().postDataJSON();
    state.requests.push({
      method: route.request().method(),
      path: new URL(route.request().url()).pathname,
      body,
    });
    state.downloaded = true;
    return route.fulfill({
      json: { id: vehicleId, lastTachoDownloadAt: body.date } satisfies Schema["TruckTachoDownloadResult"],
    });
  });
  return state;
}

test("maintenance list shows overdue and km as of", async ({ page }) => {
  const state = await setup(page);
  state.items.push({ ...maintenance, id: vehicleId, type: "tyres", overdue: false, kmReadingStale: true });
  await page.goto("/fleet/maintenance");
  const rows = page.locator("tbody tr");
  await expect(rows).toHaveCount(2);
  await expect(rows.first()).toContainText("Overdue");
  await expect(rows.first()).toContainText("125000 km as of 08.11.");
  await expect(rows.nth(1)).toContainText("km reading older than 30 days");
  await page.getByLabel("Overdue only", { exact: true }).check();
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("Service");
  await page.getByLabel("Overdue only", { exact: true }).uncheck();
  await page.getByLabel("Type", { exact: true }).selectOption("tyres");
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("Tyres");
});

test("plan maintenance from an a06 card", async ({ page }) => {
  const state = await setup(page);
  await page.goto("/");
  await page.locator(".attention-card").getByRole("link", { name: "Schedule", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByLabel("Vehicle", { exact: true })).toHaveValue(vehicleId);
  await expect(dialog.getByLabel("Due km", { exact: true })).toHaveValue("120000");
  await dialog.getByLabel("Scheduled for", { exact: true }).fill("2026-11-12");
  await dialog.getByLabel("Workshop", { exact: true }).fill("North workshop");
  await dialog.getByRole("button", { name: "Save", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.requests.at(-1)).toEqual({
    method: "PUT",
    path: `/api/v1/fleet/maintenance/${maintenanceId}`,
    body: {
      vehicleId,
      type: "service",
      scheduledFor: "2026-11-12",
      workshop: "North workshop",
      dueDate: "2026-11-09",
      dueKm: 120000,
    },
  });
});

test("done service proposes the next one", async ({ page }) => {
  const state = await setup(page);
  await page.goto("/fleet/maintenance");
  await page.getByRole("button", { name: "Done", exact: true }).click();
  await page.getByLabel("Odometer (km, optional)").fill("125000");
  await page.getByRole("dialog").getByRole("button", { name: "Save", exact: true }).click();
  const proposal = page.getByRole("dialog");
  await expect(proposal.getByLabel("Vehicle", { exact: true })).toHaveValue(vehicleId);
  await expect(proposal.getByLabel("Due date", { exact: true })).toHaveValue("2027-11-10");
  await expect(proposal.getByLabel("Due km", { exact: true })).toHaveValue("175000");
  expect(state.requests).toHaveLength(1);
  expect(state.requests[0]).toMatchObject({
    path: `/api/v1/fleet/maintenance/${maintenanceId}/done`,
    body: { doneAt: "2026-11-10", odometerAtDoneKm: 125000 },
  });
  await expect(proposal.getByRole("button", { name: "Not now", exact: true })).toBeVisible();
  await proposal.getByLabel("Workshop", { exact: true }).fill("Central workshop");
  await proposal.getByRole("button", { name: "Save", exact: true }).click();
  await expect(proposal).toHaveCount(0);
  expect(state.requests.at(-1)).toMatchObject({
    method: "POST",
    path: "/api/v1/fleet/maintenance",
    body: { vehicleId, type: "service", dueDate: "2027-11-10", dueKm: 175000, scheduledFor: "2027-11-10" },
  });
  await expect(page.locator("tbody tr")).toHaveCount(2);
});

test("cancel asks for a reason", async ({ page }) => {
  const state = await setup(page);
  await page.goto("/fleet/maintenance");
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Save", exact: true }).click();
  await expect(dialog).toBeVisible();
  expect(state.requests).toHaveLength(0);
  await expect(dialog.getByLabel("Reason", { exact: true })).toHaveAttribute("required", "");
  await dialog.getByLabel("Reason", { exact: true }).fill("Workshop unavailable");
  await dialog.getByRole("button", { name: "Save", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator("tbody tr")).toContainText("Cancelled");
  expect(state.requests.at(-1)).toEqual({
    method: "POST",
    path: `/api/v1/fleet/maintenance/${maintenanceId}/cancel`,
    body: { reason: "Workshop unavailable" },
  });
});

test("log tacho download clears the card", async ({ page }) => {
  const state = await setup(page, "A-12");
  await page.goto("/");
  await expect(page.locator(".attention-card")).toContainText("Tacho download overdue");
  await page.getByRole("button", { name: "Log download", exact: true }).click();
  await page.getByLabel("Download date", { exact: true }).fill("2026-11-09");
  await page.getByRole("dialog").getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.locator(".attention-card")).toHaveCount(0);
  expect(state.requests).toEqual([
    {
      method: "POST",
      path: `/api/v1/fleet/vehicles/${vehicleId}/tacho-downloads`,
      body: { date: "2026-11-09" },
    },
  ]);
});
