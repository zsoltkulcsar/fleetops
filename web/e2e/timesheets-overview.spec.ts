import { test, expect } from "./fixtures";
import AxeBuilder from "@axe-core/playwright";
import { officeSession } from "./people-helpers";
import { timesheetRoutes, week } from "./timesheets-helpers";

test.beforeEach(async ({ page }) => {
  await officeSession(page, ["hr"]);
  await timesheetRoutes(page);
});

test("week cards total the hours, absences and drivers to approve", async ({ page }) => {
  await page.goto("/timesheets");
  const cards = page.getByRole("region", { name: "This week at a glance" });
  await expect(cards).toContainText("Hours worked");
  await expect(cards).toContainText("8:00 h");
  await expect(cards).toContainText("Absence days");
  await expect(cards).toContainText(`${week().counts.awaiting} drivers`);
  await expect(cards.getByText("Coming in Epic 15")).toHaveCount(2);
});

test("a row with submitted shifts can be approved from the status column", async ({ page }) => {
  await page.goto("/timesheets");
  await page.getByRole("button", { name: "Approve week of Dana Driver" }).click();
  await expect(page.getByRole("dialog", { name: "Approve shifts" })).toContainText(
    "Approve 1 submitted shifts",
  );
});

test("approve all on this page opens the approval for every waiting row", async ({ page }) => {
  await page.goto("/timesheets");
  await page.getByRole("button", { name: "Approve all on this page" }).click();
  await expect(page.getByRole("dialog", { name: "Approve shifts" })).toBeVisible();
});

test("day cells carry a colour class and the legend explains them", async ({ page }) => {
  await page.goto("/timesheets");
  const cells = page.getByRole("table", { name: "Driver week grid" }).locator(".ts-cell");
  await expect(cells.nth(0)).toHaveClass(/day-awaiting/);
  await expect(cells.nth(1)).toHaveClass(/day-absence/);
  await expect(cells.nth(2)).toHaveClass(/day-missing/);
  await expect(page.getByLabel("Cell colours")).toContainText("Waiting for approval");
});

test("timesheets overview has no accessibility violations", async ({ page }) => {
  await page.goto("/timesheets");
  await expect(page.getByRole("table", { name: "Driver week grid" })).toBeVisible();
  const result = await new AxeBuilder({ page: page as never })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
    .analyze();
  expect(result.violations).toEqual([]);
});
