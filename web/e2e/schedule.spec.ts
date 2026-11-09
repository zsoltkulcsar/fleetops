import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import AxeBuilder from "@axe-core/playwright";

const cycle: components["schemas"]["CycleDetail"] = {
  id: "00000000-0000-0000-0000-000000000032",
  employeeId: "00000000-0000-0000-0000-000000000031",
  employeeName: "Dana Driver",
  vehicleId: "00000000-0000-0000-0000-000000000030",
  vehiclePlate: "B-TEST 32",
  from: "2026-11-09",
  to: "2026-11-22",
  status: "planned",
  source: "generated",
  patternSlot: 0,
  version: 1,
  hasClash: false,
};
test("invalid schedule range falls back safely and deep link opens its cycle", async ({ page }) => {
  await routes(page);
  await page.goto("/schedule?from=2026-99-99");
  await expect(page.getByRole("heading", { name: "Schedule", exact: true })).toBeVisible();
  await expect(page.getByText("This section could not be displayed")).toHaveCount(0);
  await page.goto(`/schedule?from=2026-11-09&cycleId=${cycle.id}`);
  await expect(page.getByRole("dialog")).toBeVisible();
});
async function routes(page: Page, items = [cycle]) {
  const result: components["schemas"]["ScheduleResult"] = { generatedAt: "2026-11-09T10:00:00Z", items };
  await page.route("**/api/v1/schedule?*", (route) => route.fulfill({ json: result }));
  const drivers: components["schemas"]["DriverReadinessList"] = {
    items: [{ id: cycle.employeeId, name: cycle.employeeName, canDrive: true, causes: [], enrolled: true }],
    nextCursor: null,
  };
  await page.route("**/api/v1/readiness/drivers?*", (route) => route.fulfill({ json: drivers }));
  const trucks: components["schemas"]["VehicleListResult"] = {
    items: [
      {
        id: cycle.vehicleId,
        plate: cycle.vehiclePlate,
        kind: "truck",
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
  };
  await page.route("**/api/v1/fleet/vehicles?*", (route) => route.fulfill({ json: trucks }));
  const pattern: components["schemas"]["CyclePatternView"] = {
    employeeId: cycle.employeeId,
    version: 2,
    pattern: { onWeeks: 2, offWeeks: 2, anchorDate: "2026-11-09", vehicleId: cycle.vehicleId },
  };
  await page.route("**/api/v1/people/*/cycle-pattern", (route) => route.fulfill({ json: pattern }));
}
test("schedule groups timeline by truck and driver and keeps range in URL", async ({ page }) => {
  await routes(page);
  await page.goto("/schedule?from=2026-11-09");
  await expect(page.getByRole("heading", { name: "Schedule", exact: true })).toBeVisible();
  await expect(
    page.getByRole("region", { name: "Schedule timeline" }).getByText(cycle.vehiclePlate),
  ).toBeVisible();
  await page.getByRole("button", { name: "By driver", exact: true }).click();
  await expect(page).toHaveURL(/view=driver/);
  await expect(page.locator(".schedule-subject")).toHaveText(cycle.employeeName);
  await page.getByRole("button", { name: "Next four weeks" }).click();
  await expect(page).toHaveURL(/from=2026-12-07/);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
test("dispatcher adds a manual cycle with selected driver truck dates and reason", async ({ page }) => {
  await routes(page);
  let request: components["schemas"]["ManualCycleRequest"] | undefined;
  await page.route("**/api/v1/schedule/cycles", (route) => {
    request = route.request().postDataJSON();
    return route.fulfill({
      json: { ...cycle, source: "manual" } satisfies components["schemas"]["CycleDetail"],
    });
  });
  await page.goto("/schedule?from=2026-11-09");
  await page.getByRole("button", { name: "Add cycle" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Driver", { exact: true }).selectOption(cycle.employeeId);
  await dialog.getByLabel("Truck", { exact: true }).selectOption(cycle.vehicleId);
  await dialog.getByLabel("From", { exact: true }).fill("2026-11-16");
  await dialog.getByLabel("To", { exact: true }).fill("2026-11-22");
  await dialog.getByLabel("Reason").fill("Stand-in driver");
  await dialog.getByRole("button", { name: "Save cycle" }).click();
  await expect(page.getByRole("status")).toContainText("Schedule saved");
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
  let request: components["schemas"]["EditCycleRequest"] | undefined;
  await page.route("**/api/v1/schedule/cycles/*", (route) => {
    request = route.request().postDataJSON();
    return route.fulfill({
      json: { ...cycle, source: "manual", version: 2 } satisfies components["schemas"]["CycleDetail"],
    });
  });
  await page.goto("/schedule?from=2026-11-09");
  await page.locator(".schedule-cycle").click();
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
  let request: components["schemas"]["SaveCyclePatternRequest"] | undefined;
  await page.route("**/api/v1/people/*/cycle-pattern", (route) => {
    if (route.request().method() === "GET") return route.fallback();
    request = route.request().postDataJSON();
    return route.fulfill({
      json: {
        employeeId: cycle.employeeId,
        version: 3,
        generated: 3,
      } satisfies components["schemas"]["CyclePatternResult"],
    });
  });
  await page.goto("/schedule?from=2026-11-09");
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
test("HR can inspect cycles without edit controls", async ({ page }) => {
  await routes(page);
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({ json: { employeeId: "hr", roles: ["hr"] } }),
  );
  await page.goto("/schedule?from=2026-11-09");
  await expect(page.getByRole("button", { name: "Add cycle" })).toHaveCount(0);
  await page.locator(".schedule-cycle").click();
  await expect(page.getByRole("dialog")).toContainText("A dispatcher can change it");
  await expect(page.getByRole("button", { name: "Save cycle" })).toHaveCount(0);
});
test("schedule renders empty error and conflict states without discarding the form", async ({ page }) => {
  await routes(page, []);
  await page.goto("/schedule?from=2026-11-09");
  await expect(page.getByRole("heading", { name: "No cycles in these dates" })).toBeVisible();
  await page.route("**/api/v1/schedule?*", (route) =>
    route.fulfill({ status: 503, json: { code: "unavailable" } }),
  );
  await page.reload();
  await expect(page.getByText("Schedule could not be loaded")).toBeVisible();
  await routes(page);
  await page.reload();
  await page.route("**/api/v1/schedule/cycles/*", (route) =>
    route.fulfill({ status: 409, json: { code: "version_conflict" } }),
  );
  await page.locator(".schedule-cycle").click();
  await page.getByLabel("Reason").fill("Keep this reason");
  await page.getByRole("button", { name: "Save cycle" }).click();
  await expect(page.getByRole("dialog")).toContainText("The plan changed");
  await expect(page.getByLabel("Reason")).toHaveValue("Keep this reason");
});
for (const theme of ["light", "dark"] as const)
  test(`schedule ${theme} theme keyboard and accessibility`, async ({ page }) => {
    await routes(page, [{ ...cycle, hasClash: true }]);
    await page.goto("/schedule?from=2026-11-09");
    await page.evaluate((theme) => (document.documentElement.dataset.theme = theme), theme);
    await expect(page.locator(".schedule-cycle")).toContainText("Clash");
    await page.locator(".schedule-cycle").focus();
    await expect(page.locator(".schedule-cycle")).toBeFocused();
    const result = await new AxeBuilder({ page: page as unknown as import("playwright-core").Page })
      .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
      .analyze();
    expect(result.violations).toEqual([]);
  });
