import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import type { Session } from "../apps/office/src/auth/api";
import AxeBuilder from "@axe-core/playwright";
type Schema = components["schemas"];
const vehicleId = "00000000-0000-0000-0000-000000000030";
const employeeId = "00000000-0000-0000-0000-000000000031";
const defectId = "00000000-0000-0000-0000-000000000090";
const repairId = "00000000-0000-0000-0000-000000000091";
const replacementId = "00000000-0000-0000-0000-000000000092";
const row: Schema["DefectRow"] = {
  id: defectId,
  vehicleId,
  plate: "B-TEST 32",
  category: "brakes",
  severity: "critical",
  reportedBy: employeeId,
  reportedByName: "Dana Driver",
  reportedAt: "2026-11-09T10:00:00Z",
  photosCount: 1,
  status: "open",
  ageDays: 1,
  origin: "app",
};
const repair: Schema["RepairDetail"] = {
  id: repairId,
  type: "repair",
  vehicleId,
  workshop: "Central workshop",
  scheduledFor: "2026-11-11",
  defectReportIds: [defectId],
  status: "scheduled",
  cancelReason: null,
  doneAt: null,
  odometerAtDoneKm: null,
  inWorkshop: false,
};
async function setup(page: Page) {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      json: { employeeId, roles: ["dispatcher"], stepUpMethod: "password" } satisfies Session,
    }),
  );
  await page.clock.setFixedTime(new Date("2026-11-10T10:00:00Z"));
  const state = {
    detail: {
      report: { ...row },
      description: "Brake pressure lost",
      repairId: null,
      closedBy: null,
      closedAt: null,
      closeReason: null,
      photos: [
        {
          id: defectId,
          link: { url: "https://photos.example.test/brake.png", expiresAt: "2026-11-11T10:00:00Z" },
        },
      ],
      history: [],
    } as Schema["DefectDetail"],
    rows: [
      row,
      {
        ...row,
        id: repairId,
        plate: "B-CHAT 10",
        severity: "minor",
        status: "acknowledged",
        origin: "office_from_chat",
        photosCount: 0,
      } satisfies Schema["DefectRow"],
    ],
    repairs: [] as Schema["RepairDetail"][],
    options: [
      {
        vehicleId: replacementId,
        plate: "B-SWAP 20",
        belongsTo: "Alex Owner",
        runningShiftUserName: "Lee Driver",
        runningShiftStartAt: "2026-11-10T08:00:00Z",
      },
    ] as Schema["SwapOption"][],
    assignment: undefined as Schema["AssignmentDetail"] | undefined,
    requests: [] as { path: string; body: unknown }[],
    queries: [] as URLSearchParams[],
  };
  await page.route("https://photos.example.test/**", (route) =>
    route.fulfill({
      contentType: "image/svg+xml",
      body: '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10" fill="red"/></svg>',
    }),
  );
  await page.route("**/api/v1/fleet/vehicles?*", (route) =>
    route.fulfill({
      json: {
        items: [
          {
            id: vehicleId,
            kind: "truck",
            plate: row.plate,
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
  await page.route("**/api/v1/timesheets/drivers?*", (route) =>
    route.fulfill({
      json: {
        items: [{ id: employeeId, name: "Dana Driver", personnelNumber: "D-100" }],
      } satisfies Schema["TimesheetDrivers"],
    }),
  );
  await page.route("**/api/v1/fleet/defects**", (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === "GET") {
      if (url.pathname.endsWith(defectId)) return route.fulfill({ json: state.detail });
      state.queries.push(url.searchParams);
      return route.fulfill({
        json: {
          items: state.rows,
          total: state.rows.length,
          page: 1,
          pageSize: 25,
          openByOrigin: { app: 1, officeFromChat: 1 },
        } satisfies Schema["DefectListResult"],
      });
    }
    const body: unknown = request.postData() ? request.postDataJSON() : null;
    state.requests.push({ path: url.pathname, body });
    if (url.pathname.endsWith("/confirm")) state.detail.report.status = "acknowledged";
    else if (url.pathname.endsWith("/downgrade"))
      state.detail.report.severity = (body as Schema["DowngradeDefectRequest"]).severity;
    else if (url.pathname.endsWith("/close")) {
      state.detail.report.status = "closed";
      state.detail.closeReason = (body as Schema["CloseDefectRequest"]).reason;
    } else state.detail.report.origin = "office_from_chat";
    return route.fulfill({
      json: {
        id: defectId,
        status: state.detail.report.status,
        severity: state.detail.report.severity,
      } satisfies Schema["DefectActionResult"],
    });
  });
  await page.route("**/api/v1/fleet/maintenance**", (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === "GET")
      return route.fulfill({ json: { items: state.repairs } satisfies Schema["MaintenanceList"] });
    const body: unknown = request.postData() ? request.postDataJSON() : null;
    state.requests.push({ path, body });
    if (path.endsWith("/start")) {
      state.repairs[0].status = "in_progress";
      state.repairs[0].inWorkshop = true;
    } else if (path.endsWith("/done"))
      Object.assign(state.repairs[0], body, { status: "done", inWorkshop: false });
    else if (path.endsWith("/cancel")) {
      state.repairs[0].status = "cancelled";
      state.repairs[0].cancelReason = (body as Schema["CancelRepairRequest"]).reason;
    } else {
      state.repairs = [{ ...repair }];
      state.detail.repairId = repairId;
      state.detail.report.status = "in_repair";
    }
    return route.fulfill({ json: state.repairs[0] });
  });
  await page.route("**/api/v1/fleet/swap-options?*", (route) =>
    route.fulfill({ json: { items: state.options } satisfies Schema["SwapOptions"] }),
  );
  await page.route("**/api/v1/fleet/assignments", (route) => {
    const body: Schema["CreateAssignmentCommand"] = route.request().postDataJSON();
    state.requests.push({ path: new URL(route.request().url()).pathname, body });
    state.assignment = {
      id: replacementId,
      employeeId: body.employeeId,
      vehicleId: body.vehicleId,
      from: "2026-11-10",
      to: body.to ?? null,
      maintenanceId: body.maintenanceId ?? null,
    };
    return route.fulfill({ json: state.assignment });
  });
  await page.route("**/api/v1/schedule?*", (route) =>
    route.fulfill({
      json: {
        generatedAt: "2026-11-10T10:00:00Z",
        items: [],
        swaps: state.assignment
          ? [{ ...state.assignment, employeeName: "Dana Driver", vehiclePlate: "B-SWAP 20", label: "Swap" }]
          : [],
      } satisfies Schema["ScheduleResult"],
    }),
  );
  return state;
}
async function openDetail(page: Page) {
  await page.goto(`/fleet/defects?defectId=${defectId}`);
  await expect(page.getByRole("dialog").getByText("Brake pressure lost", { exact: true })).toBeVisible();
}
async function openSwap(page: Page) {
  await openDetail(page);
  await page.getByRole("button", { name: "Swap driver", exact: true }).click();
  await page.getByLabel("Driver", { exact: true }).selectOption(employeeId);
}
test("defects tab lists every defect with severity, status, origin and photos", async ({ page }) => {
  const state = await setup(page);
  await page.goto("/fleet/defects");
  await expect(page.getByRole("heading", { name: "Fleet \u00b7 Defects", exact: true })).toBeVisible();
  await expect(page.locator("tbody tr")).toHaveCount(state.rows.length);
  const first = page.locator("tbody tr").first();
  for (const text of ["Critical", "Open", "App", "B-TEST 32", "Dana Driver"])
    await expect(first).toContainText(text);
  await expect(first.getByRole("cell").nth(4)).toHaveText("1");
  await expect(page.locator("tbody tr").nth(1)).toContainText("Logged from chat");
  const second = page.locator("tbody tr").nth(1);
  await expect(second).toContainText("Minor");
  await expect(second).toContainText("Acknowledged");
  await expect(second).toContainText("B-CHAT 10");
  await expect(second.getByRole("cell").nth(4)).toHaveText("0");
  await first.getByRole("button").click();
  await expect(page.getByRole("img", { name: "Defect photo 1" })).toBeVisible();
});
test("defect filters ask the server", async ({ page }) => {
  const state = await setup(page);
  await page.goto("/fleet/defects");
  await page.getByLabel("Status", { exact: true }).selectOption("closed");
  await page.getByLabel("Severity", { exact: true }).selectOption("minor");
  await expect.poll(() => state.queries.at(-1)?.get("severity")).toBe("minor");
  expect(state.queries.at(-1)?.get("statusGroup")).toBe("closed");
  expect(state.queries.at(-1)?.get("page")).toBe("1");
});
test("confirm and downgrade with a reason", async ({ page }) => {
  const state = await setup(page);
  await openDetail(page);
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(dialog).toContainText("Acknowledged");
  expect(state.requests.at(-1)?.path).toBe(`/api/v1/fleet/defects/${defectId}/confirm`);
  await dialog.getByRole("button", { name: "Downgrade", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Save", exact: true })).toBeDisabled();
  await dialog.getByLabel("New severity").selectOption("major");
  await dialog.getByLabel("Reason", { exact: true }).fill("Workshop checked brakes");
  await dialog.getByRole("button", { name: "Save", exact: true }).click();
  await expect(dialog).toContainText("Major");
  expect(state.requests.at(-1)).toEqual({
    path: `/api/v1/fleet/defects/${defectId}/downgrade`,
    body: { severity: "major", reason: "Workshop checked brakes" },
  });
});
test("close needs a reason and is hidden when a repair is linked", async ({ page }) => {
  const state = await setup(page);
  await openDetail(page);
  await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("button", { name: "Save", exact: true })).toBeDisabled();
  await page.getByLabel("Reason", { exact: true }).fill("Duplicate report checked");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByRole("dialog")).toContainText("Closed: Duplicate report checked");
  expect(state.requests.at(-1)?.body).toEqual({ reason: "Duplicate report checked" });
  state.detail.report.status = "in_repair";
  state.detail.repairId = repairId;
  state.repairs = [{ ...repair }];
  await page.reload();
  await expect(page.getByRole("dialog")).toContainText("Linked repair: Central workshop");
  await expect(page.getByRole("dialog").getByRole("button", { name: "Close", exact: true })).toHaveCount(0);
});
test("log a report from chat", async ({ page }) => {
  const state = await setup(page);
  await page.goto("/fleet/defects");
  await page.getByRole("button", { name: "Log a report", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Vehicle", { exact: true }).selectOption(vehicleId);
  await dialog.getByLabel("Category", { exact: true }).selectOption("brakes");
  await dialog.getByLabel("Severity", { exact: true }).selectOption("critical");
  await dialog.getByLabel("Description", { exact: true }).fill("Brake pressure lost");
  await dialog.getByLabel("Reported by", { exact: true }).selectOption(employeeId);
  await dialog.getByRole("button", { name: "Log report", exact: true }).click();
  await expect(dialog.getByText("Logged from chat", { exact: true })).toBeVisible();
  expect(state.requests.at(-1)?.body).toEqual({
    vehicleId,
    category: "brakes",
    severity: "critical",
    description: "Brake pressure lost",
    reportedBy: employeeId,
    photos: [],
  });
});
test("create a repair from a defect and mark it done", async ({ page }) => {
  const state = await setup(page);
  await openDetail(page);
  await page.getByRole("button", { name: "Create repair", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Workshop", { exact: true }).fill("Central workshop");
  await dialog.getByLabel("Scheduled date", { exact: true }).fill("2026-11-11");
  await expect(dialog.getByRole("checkbox", { name: "brakes (critical)" })).toBeChecked();
  await dialog.getByRole("button", { name: "Create repair", exact: true }).click();
  await expect(dialog).toContainText("Linked repair: Central workshop");
  expect(state.requests.at(-1)?.body).toEqual({
    vehicleId,
    type: "repair",
    workshop: "Central workshop",
    scheduledFor: "2026-11-11",
    defectReportIds: [defectId],
  });
  await page.goto("/fleet/maintenance");
  await page.getByRole("button", { name: "Done", exact: true }).click();
  await page.getByLabel("Done date", { exact: true }).fill("2026-11-12");
  await page.getByLabel("Odometer (km, optional)").fill("125000");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.locator("tbody tr")).toContainText("125000 km");
  expect(state.requests.at(-1)).toEqual({
    path: `/api/v1/fleet/maintenance/${repairId}/done`,
    body: { doneAt: "2026-11-12", odometerAtDoneKm: 125000, costEur: null, resultDocument: null },
  });
});
test("maintenance tab lists repairs with start, done and cancel", async ({ page }) => {
  const state = await setup(page);
  state.repairs = [{ ...repair }];
  await page.goto("/fleet/maintenance");
  const current = page.locator("tbody tr");
  await expect(current).toContainText("Central workshop");
  await expect(current).toContainText("11.11.2026");
  for (const name of ["Start", "Done", "Cancel"])
    await expect(current.getByRole("button", { name, exact: true })).toBeVisible();
  await current.getByRole("button", { name: "Start", exact: true }).click();
  await expect(current).toContainText("In progress");
  expect(state.requests.at(-1)?.path).toBe(`/api/v1/fleet/maintenance/${repairId}/start`);
  await current.getByRole("button", { name: "Cancel", exact: true }).click();
  await page.getByLabel("Reason", { exact: true }).fill("Workshop unavailable");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(current).toContainText("Cancelled");
  await expect(current).toContainText("Workshop unavailable");
  expect(state.requests.at(-1)?.body).toEqual({ reason: "Workshop unavailable" });
});
test("swap picker lists usable trucks with owner and in use since", async ({ page }) => {
  await setup(page);
  await openSwap(page);
  await expect(
    page.getByRole("radio", { name: "B-SWAP 20 Belongs to Alex Owner In use by Lee Driver since 09:00" }),
  ).toBeVisible();
  await expect(page.getByRole("radio")).toHaveCount(1);
});
test("swap picker says when no truck is usable and links to fleet", async ({ page }) => {
  const state = await setup(page);
  state.options = [];
  await openSwap(page);
  await expect(page.getByText("No usable truck right now.", { exact: false })).toBeVisible();
  await expect(
    page.getByRole("dialog").getByRole("link", { name: "Open Fleet", exact: true }),
  ).toHaveAttribute("href", "/fleet");
  await expect(page.getByRole("button", { name: "Save swap", exact: true })).toBeDisabled();
});
test("a saved swap shows on the schedule", async ({ page }) => {
  const state = await setup(page);
  await openSwap(page);
  await page.getByRole("radio").check();
  await page.getByRole("button", { name: "Save swap", exact: true }).click();
  await expect(page.getByText("Swap saved. Affected drivers will be told.", { exact: true })).toBeVisible();
  expect(state.requests.at(-1)?.body).toEqual({
    employeeId,
    vehicleId: replacementId,
    to: null,
    maintenanceId: null,
  });
  await page.getByRole("link", { name: "Open Plan", exact: true }).click();
  await expect(page.locator('.plan-block[data-kind="swap"]')).toContainText("Swap");
  await expect(page.getByRole("region", { name: "Plan timeline" })).toContainText("B-SWAP 20");
});
test("defects and maintenance tabs have no accessibility violations", async ({ page }) => {
  const state = await setup(page);
  state.repairs = [{ ...repair }];
  for (const tab of ["Defects", "Maintenance"]) {
    await page.goto(`/fleet/${tab.toLowerCase()}`);
    await expect(page.getByRole("heading", { name: `Fleet \u00b7 ${tab}`, exact: true })).toBeVisible();
    await expect(page.locator("tbody tr").first()).toBeVisible();
    const result = await new AxeBuilder({ page: page as unknown as import("playwright-core").Page })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(result.violations).toEqual([]);
  }
});
test("defect rows and detail show labels, not wire values", async ({ page }) => {
  const state = await setup(page);
  state.rows[0] = { ...state.rows[0], category: "load_securing" };
  state.detail.report = { ...state.detail.report, category: "load_securing" };
  state.detail.history = [
    {
      fieldName: "Photos",
      oldValue: null,
      newValue: "1 added",
      actorId: employeeId,
      reason: "Reported in the app",
      at: "2026-11-09T10:00:00Z",
    },
  ];
  await page.goto("/fleet/defects");
  const first = page.locator("tbody tr").first();
  await expect(first.getByRole("cell").nth(2)).toHaveText("Load securing");
  await expect(first).not.toContainText("load_securing");
  await expect(first).not.toContainText("load securing load securing");
  await first.getByRole("button").click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: "B-TEST 32: Load securing" })).toBeVisible();
  await expect(dialog.getByRole("listitem").filter({ hasText: "Photos" })).toHaveText(
    "09.11.2026 11:00: Photos 1 added · Reported in the app",
  );
  await expect(dialog).not.toContainText("System.Guid");
});
