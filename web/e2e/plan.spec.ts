import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import AxeBuilder from "@axe-core/playwright";

type Schema = components["schemas"];
const id = (value: number) => `00000000-0000-0000-0000-${String(value).padStart(12, "0")}`;
const cycle: Schema["CycleDetail"] = {
  id: id(32),
  employeeId: id(31),
  employeeName: "Dana Driver",
  vehicleId: id(30),
  vehiclePlate: "B-TEST 32",
  from: "2026-11-09",
  to: "2026-11-22",
  status: "planned",
  source: "generated",
  patternSlot: 0,
  version: 1,
  hasClash: false,
};
const truck = (vehicleId: string, plate: string, usable = true): Schema["VehicleListItem"] => ({
  id: vehicleId,
  plate,
  kind: "truck",
  make: "MAN",
  model: "TGX",
  year: 2024,
  status: "active",
  ownership: "owned",
  version: 1,
  shownStatus: usable ? "active" : "off_road",
  usable,
  causes: [],
});
const driver = (employeeId: string, name: string, canDrive = true): Schema["DriverReadinessItem"] => ({
  id: employeeId,
  name,
  canDrive,
  causes: [],
  enrolled: true,
});
type Setup = {
  items?: Schema["CycleDetail"][];
  trucks?: Schema["VehicleListItem"][];
  drivers?: Schema["DriverReadinessItem"][];
  requests?: Schema["ScheduleRequestView"][];
};
async function routes(page: Page, setup: Setup = {}) {
  const result: Schema["ScheduleResult"] = {
    generatedAt: "2026-11-09T10:00:00Z",
    items: setup.items ?? [cycle],
  };
  await page.route("**/api/v1/schedule?*", (route) => route.fulfill({ json: result }));
  const drivers: Schema["DriverReadinessList"] = {
    items: setup.drivers ?? [driver(cycle.employeeId, cycle.employeeName)],
    nextCursor: null,
  };
  await page.route("**/api/v1/readiness/drivers?*", (route) => route.fulfill({ json: drivers }));
  const trucks: Schema["VehicleListResult"] = {
    items: setup.trucks ?? [truck(cycle.vehicleId, cycle.vehiclePlate)],
    nextCursor: null,
  };
  await page.route("**/api/v1/fleet/vehicles?*", (route) => route.fulfill({ json: trucks }));
  const requests: Schema["ScheduleRequestList"] = { items: setup.requests ?? [] };
  await page.route("**/api/v1/schedule/requests", (route) => route.fulfill({ json: requests }));
  const pattern: Schema["CyclePatternView"] = {
    employeeId: cycle.employeeId,
    version: 2,
    pattern: { onWeeks: 2, offWeeks: 2, anchorDate: "2026-11-09", vehicleId: cycle.vehicleId },
  };
  await page.route("**/api/v1/people/*/cycle-pattern", (route) => route.fulfill({ json: pattern }));
}
// Problems are judged against today, so the tests fix the day inside the shown four weeks.
async function open(page: Page, url = "/plan?from=2026-11-09") {
  await page.clock.install({ time: new Date("2026-11-11T09:00:00Z") });
  await page.goto(url);
}
const side = (page: Page) => page.getByRole("complementary", { name: "Selected truck" });

test("invalid plan range falls back safely and deep link opens its cycle", async ({ page }) => {
  await routes(page);
  await page.goto("/plan?from=2026-99-99");
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await expect(page.getByText("This section could not be displayed")).toHaveCount(0);
  await page.goto(`/plan?from=2026-11-09&cycleId=${cycle.id}`);
  await expect(page.getByRole("dialog")).toBeVisible();
});
test("old schedule links land on the plan and keep their query", async ({ page }) => {
  await routes(page);
  await page.goto(`/schedule?from=2026-11-09&cycleId=${cycle.id}`);
  await expect(page).toHaveURL(new RegExp(`/plan\\?from=2026-11-09&cycleId=${cycle.id}`));
  await expect(page.getByRole("dialog")).toBeVisible();
});
test("plan is its own page in the navigation and no longer a fleet tab", async ({ page }) => {
  await routes(page);
  await open(page);
  await expect(page).toHaveTitle(/^Plan/);
  if (page.viewportSize()!.width >= 768) {
    await expect(page.locator('nav[aria-label="Primary"] a[aria-current="page"]')).toHaveText("Plan");
  } else {
    await page.getByRole("button", { name: "More" }).click();
    await expect(
      page.locator("#mobile-more-links").getByRole("link", { name: "Plan", exact: true }),
    ).toBeVisible();
  }
  await page.goto("/fleet");
  const tabs = page.getByRole("group", { name: "Fleet views" });
  await expect(tabs.getByRole("link", { name: /^Vehicles/ })).toBeVisible();
  await expect(tabs.getByRole("link", { name: "Schedule", exact: true })).toHaveCount(0);
});
test("plan groups timeline by truck and driver and keeps range in URL", async ({ page }) => {
  await routes(page);
  await open(page);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Every truck runs as planned.");
  const timeline = page.getByRole("region", { name: "Plan timeline" });
  await expect(timeline.locator(".plate")).toHaveText(`EU${cycle.vehiclePlate}`);
  await expect(timeline.getByText("today", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "By driver", exact: true }).click();
  await expect(page).toHaveURL(/view=driver/);
  await expect(page.locator(".plan-subject")).toHaveText(cycle.employeeName);
  await page.getByRole("button", { name: "Next four weeks" }).click();
  await expect(page).toHaveURL(/from=2026-12-07/);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
test("a truck without problems shows its driver and next handover when selected", async ({ page }) => {
  const next: Schema["CycleDetail"] = {
    ...cycle,
    id: id(33),
    employeeId: id(34),
    employeeName: "Nils Next",
    from: "2026-11-23",
    to: "2026-12-06",
  };
  await routes(page, { items: [cycle, next] });
  await open(page);
  await expect(side(page)).toContainText("No truck selected");
  await page.getByRole("button", { name: `${cycle.vehiclePlate}, As planned` }).click();
  await expect(page).toHaveURL(new RegExp(`truck=${cycle.vehicleId}`));
  await expect(side(page).getByRole("heading", { name: cycle.employeeName })).toBeVisible();
  await expect(side(page).locator(".plate")).toHaveText(`EU${cycle.vehiclePlate}`);
  await expect(side(page)).toContainText("Dana Driver, until 22.11.");
  await expect(side(page)).toContainText("Nils Next from 23.11. to 06.12.");
  await expect(side(page)).toContainText("MAN TGX");
  await expect(side(page).getByRole("link", { name: "Open vehicle record" })).toHaveAttribute(
    "href",
    `/fleet/${cycle.vehicleId}`,
  );
});
test("a truck whose driver is blocked is selected first and one tap assigns a free driver", async ({
  page,
}) => {
  const calm: Schema["CycleDetail"] = {
    ...cycle,
    id: id(40),
    employeeId: id(41),
    employeeName: "Carla Calm",
    vehicleId: id(42),
    vehiclePlate: "A-CALM 1",
  };
  await routes(page, {
    items: [calm, cycle],
    trucks: [truck(calm.vehicleId, calm.vehiclePlate), truck(cycle.vehicleId, cycle.vehiclePlate)],
    drivers: [
      driver(cycle.employeeId, cycle.employeeName, false),
      driver(calm.employeeId, calm.employeeName),
      driver(id(50), "Frieda Free"),
    ],
  });
  let assigned: Schema["CreateAssignmentCommand"] | undefined;
  await page.route("**/api/v1/fleet/assignments", (route) => {
    assigned = route.request().postDataJSON();
    const detail: Schema["AssignmentDetail"] = {
      id: id(60),
      employeeId: id(50),
      vehicleId: cycle.vehicleId,
      from: "2026-11-11",
      to: cycle.to,
      maintenanceId: null,
    };
    return route.fulfill({ json: detail });
  });
  await open(page);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(
    "1 truck needs a decision. The rest run as planned.",
  );
  await expect(page.locator(".plan-lane").first()).toContainText(cycle.vehiclePlate);
  await expect(page.locator(".plan-lane").first()).toContainText("Dana Driver is blocked");
  await expect(side(page).getByRole("heading", { name: "Who takes this truck?" })).toBeVisible();
  await expect(side(page)).toContainText("Dana Driver is blocked. The truck is usable.");
  await expect(side(page).getByRole("button", { name: /Carla Calm/ })).toHaveCount(0);
  await side(page)
    .getByRole("button", { name: /Frieda Free/ })
    .click();
  await expect(page.getByRole("status")).toContainText("Frieda Free takes B-TEST 32");
  expect(assigned).toEqual({
    employeeId: id(50),
    vehicleId: cycle.vehicleId,
    to: cycle.to,
    maintenanceId: null,
  });
});
test("an off road truck with a driver and a late handover each ask for a decision", async ({ page }) => {
  const late: Schema["CycleDetail"] = {
    ...cycle,
    id: id(70),
    employeeId: id(71),
    employeeName: "Lars Late",
    vehicleId: id(72),
    vehiclePlate: "C-LATE 7",
    from: "2026-10-27",
    to: "2026-11-09",
    status: "overrun",
  };
  await routes(page, {
    items: [cycle, late],
    trucks: [truck(cycle.vehicleId, cycle.vehiclePlate, false), truck(late.vehicleId, late.vehiclePlate)],
    drivers: [driver(cycle.employeeId, cycle.employeeName), driver(late.employeeId, late.employeeName)],
  });
  await open(page);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(
    "2 trucks need a decision. The rest run as planned.",
  );
  await expect(side(page).getByRole("heading", { name: "What happens with this truck?" })).toBeVisible();
  await expect(side(page)).toContainText("Dana Driver has no usable truck until 22.11. No repair is booked.");
  await expect(side(page).getByRole("link", { name: /Book a repair/ })).toHaveAttribute(
    "href",
    `/fleet/maintenance?vehicleId=${cycle.vehicleId}&action=plan`,
  );
  await page.getByRole("button", { name: "C-LATE 7, Handover late" }).click();
  await expect(side(page).getByRole("heading", { name: "The handover is late" })).toBeVisible();
  await expect(side(page)).toContainText("Lars Late was due back 09.11.");
  await side(page)
    .getByRole("button", { name: /Extend Lars Late/ })
    .click();
  await expect(page.getByRole("dialog").getByLabel("To", { exact: true })).toHaveValue("2026-11-09");
});
test("a change request shows beside its truck and accept and decline send the decision", async ({ page }) => {
  const request: Schema["ScheduleRequestView"] = {
    id: id(80),
    employeeId: cycle.employeeId,
    cycleId: cycle.id,
    proposedFrom: "2026-11-16",
    proposedTo: "2026-11-29",
    note: "Family event.",
    status: "open",
    declineReason: null,
    decidedBy: null,
    decidedAt: null,
  };
  await routes(page, { requests: [request] });
  const decisions: Schema["ScheduleRequestDecisionRequest"][] = [];
  await page.route("**/api/v1/schedule/requests/*/decide", (route) => {
    decisions.push(route.request().postDataJSON());
    return route.fulfill({
      json: { ...request, status: "accepted" } satisfies Schema["ScheduleRequestView"],
    });
  });
  await open(page);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(
    "1 change request to answer. Every truck runs as planned.",
  );
  const card = side(page).getByRole("region", { name: "Change request from Dana Driver" });
  await expect(card).toContainText("asks to move the cycle of 09.11. – 22.11. to 16.11. – 29.11.2026.");
  await expect(card).toContainText("“Family event.”");
  await expect(card).toContainText("Accepting changes this cycle only, not the pattern.");
  await card.getByRole("button", { name: "Decline", exact: true }).click();
  await expect(card.getByRole("button", { name: "Confirm decline" })).toBeDisabled();
  await card.getByLabel("Decline reason").fill("Truck is needed that week");
  await card.getByRole("button", { name: "Confirm decline" }).click();
  await expect(page.getByRole("status")).toContainText("Change declined");
  await card.getByRole("button", { name: "Accept", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Change accepted");
  expect(decisions).toEqual([
    { accept: false, reason: "Truck is needed that week" },
    { accept: true, reason: null },
  ]);
});
test("dispatcher adds a manual cycle with selected driver truck dates and reason", async ({ page }) => {
  await routes(page);
  let request: Schema["ManualCycleRequest"] | undefined;
  await page.route("**/api/v1/schedule/cycles", (route) => {
    request = route.request().postDataJSON();
    return route.fulfill({ json: { ...cycle, source: "manual" } satisfies Schema["CycleDetail"] });
  });
  await page.goto("/plan?from=2026-11-09");
  await page.getByRole("button", { name: "Add cycle" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Driver", { exact: true }).selectOption(cycle.employeeId);
  await dialog.getByLabel("Truck", { exact: true }).selectOption(cycle.vehicleId);
  await dialog.getByLabel("From", { exact: true }).fill("2026-11-16");
  await dialog.getByLabel("To", { exact: true }).fill("2026-11-22");
  await dialog.getByLabel("Reason").fill("Stand-in driver");
  await dialog.getByRole("button", { name: "Save cycle" }).click();
  await expect(page.getByRole("status")).toContainText("Plan saved");
  expect(request).toEqual({
    employeeId: cycle.employeeId,
    vehicleId: cycle.vehicleId,
    from: "2026-11-16",
    to: "2026-11-22",
    reason: "Stand-in driver",
  });
});
test("editing a generated cycle submits its version and explains manual preservation", async ({ page }) => {
  await routes(page);
  let request: Schema["EditCycleRequest"] | undefined;
  await page.route("**/api/v1/schedule/cycles/*", (route) => {
    request = route.request().postDataJSON();
    return route.fulfill({
      json: { ...cycle, source: "manual", version: 2 } satisfies Schema["CycleDetail"],
    });
  });
  await open(page);
  await page.locator("button.plan-block").click();
  await side(page).getByRole("button", { name: "Edit this cycle" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("Automatic rollover will keep this edit");
  await dialog.getByLabel("Reason").fill("Change handover");
  await dialog.getByRole("button", { name: "Save cycle" }).click();
  await expect(dialog).not.toBeVisible();
  expect(request?.version).toBe(1);
  expect(request?.reason).toBe("Change handover");
});
test("cycle pattern loads its current version and saves new on off weeks", async ({ page }) => {
  await routes(page);
  let request: Schema["SaveCyclePatternRequest"] | undefined;
  await page.route("**/api/v1/people/*/cycle-pattern", (route) => {
    if (route.request().method() === "GET") return route.fallback();
    request = route.request().postDataJSON();
    return route.fulfill({
      json: { employeeId: cycle.employeeId, version: 3, generated: 3 } satisfies Schema["CyclePatternResult"],
    });
  });
  await page.goto("/plan?from=2026-11-09");
  await page.getByRole("button", { name: "Cycle patterns" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Driver", { exact: true }).selectOption(cycle.employeeId);
  await expect(dialog.getByLabel("On weeks")).toHaveValue("2");
  await dialog.getByLabel("On weeks").fill("3");
  await dialog.getByRole("button", { name: "Save pattern" }).click();
  await expect(dialog).not.toBeVisible();
  expect(request?.version).toBe(2);
  expect(request?.pattern.onWeeks).toBe(3);
});
test("HR can inspect cycles without edit or decision controls", async ({ page }) => {
  await routes(page, { drivers: [driver(cycle.employeeId, cycle.employeeName, false)] });
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({ json: { employeeId: "hr", roles: ["hr"] } }),
  );
  await open(page);
  await expect(page.getByRole("button", { name: "Add cycle" })).toHaveCount(0);
  await expect(side(page)).toContainText("A dispatcher can assign a driver.");
  await expect(side(page).locator(".plan-option")).toHaveCount(0);
  await page.goto(`/plan?from=2026-11-09&cycleId=${cycle.id}`);
  await expect(page.getByRole("dialog")).toContainText("A dispatcher can change it");
  await expect(page.getByRole("button", { name: "Save cycle" })).toHaveCount(0);
});
test("plan renders empty error and conflict states without discarding the form", async ({ page }) => {
  await routes(page, { items: [], trucks: [] });
  await page.goto("/plan?from=2026-11-09");
  await expect(page.getByRole("heading", { name: "No cycles in these dates" })).toBeVisible();
  await page.route("**/api/v1/schedule?*", (route) =>
    route.fulfill({ status: 503, json: { code: "unavailable" } }),
  );
  await page.reload();
  await expect(page.getByText("The plan could not be loaded")).toBeVisible();
  await routes(page);
  await page.route("**/api/v1/schedule/cycles/*", (route) =>
    route.fulfill({ status: 409, json: { code: "version_conflict" } }),
  );
  await page.goto(`/plan?from=2026-11-09&cycleId=${cycle.id}`);
  await page.getByLabel("Reason").fill("Keep this reason");
  await page.getByRole("button", { name: "Save cycle" }).click();
  await expect(page.getByRole("dialog")).toContainText("The plan changed");
  await expect(page.getByLabel("Reason")).toHaveValue("Keep this reason");
});
for (const theme of ["light", "dark"] as const)
  test(`plan ${theme} theme keyboard and accessibility`, async ({ page }) => {
    await routes(page, {
      items: [{ ...cycle, hasClash: true }],
      drivers: [driver(cycle.employeeId, cycle.employeeName, false)],
    });
    await open(page);
    await page.evaluate((theme) => (document.documentElement.dataset.theme = theme), theme);
    await expect(side(page).getByRole("heading", { name: "Who takes this truck?" })).toBeVisible();
    await page.locator("button.plan-block").first().focus();
    await expect(page.locator("button.plan-block").first()).toBeFocused();
    const result = await new AxeBuilder({ page: page as unknown as import("playwright-core").Page })
      .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
      .analyze();
    expect(result.violations).toEqual([]);
  });
