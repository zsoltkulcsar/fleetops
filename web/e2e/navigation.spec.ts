import { test, expect } from "./fixtures";
import type { components } from "@fleetops/api-client";

const routes = [
  ["/", "Attention"],
  ["/people", "People"],
  ["/payroll", "Payroll export"],
  ["/settings", "Settings"],
] as const;

test("every route shows its empty state heading", async ({ page }) => {
  const people: components["schemas"]["DriverReadinessList"] = { items: [], nextCursor: null };
  await page.route("**/api/v1/readiness/drivers?*", (route) => route.fulfill({ json: people }));
  for (const [route, label] of routes) {
    await page.goto(route);
    await expect(page.getByRole("heading", { level: 1, name: label })).toBeVisible();
    if (route === "/")
      await expect(
        page.getByRole("heading", { level: 2, name: "Nothing needs attention right now." }),
      ).toBeVisible();
    else await expect(page.getByRole("heading", { level: 2, name: "Nothing here yet" })).toBeVisible();
  }
});
test("placeholder pages have no action buttons", async ({ page }) => {
  for (const [route] of routes) {
    await page.goto(route);
    await expect(page.locator(".empty-state button")).toHaveCount(0);
  }
});

test("mobile More link goes to Documents and the list closes", async ({ page }) => {
  test.skip((await page.evaluate(() => innerWidth)) >= 768);
  await page.goto("/");
  await page.getByRole("button", { name: "More" }).click();
  await page.getByRole("link", { name: "Documents" }).click();
  await expect(page).toHaveURL(/\/documents$/);
  await expect(page.locator("#mobile-more-links")).toHaveCount(0);
});
test("mobile More link goes to Payroll export and the list closes", async ({ page }) => {
  test.skip((await page.evaluate(() => innerWidth)) >= 768);
  await page.goto("/");
  await page.getByRole("button", { name: "More" }).click();
  await page.getByRole("link", { name: "Payroll export" }).click();
  await expect(page).toHaveURL(/\/payroll$/);
  await expect(page.locator("#mobile-more-links")).toHaveCount(0);
});
test("mobile More link goes to Settings and the list closes", async ({ page }) => {
  test.skip((await page.evaluate(() => innerWidth)) >= 768);
  await page.goto("/");
  await page.getByRole("button", { name: "More" }).click();
  await page.getByRole("link", { name: "Settings" }).click();
  await expect(page).toHaveURL(/\/settings$/);
  await expect(page.locator("#mobile-more-links")).toHaveCount(0);
});

test("escape closes mobile More and returns focus", async ({ page }) => {
  test.skip((await page.evaluate(() => innerWidth)) >= 768);
  await page.goto("/");
  const more = page.getByRole("button", { name: "More" });
  await more.click();
  await page.keyboard.press("Escape");
  await expect(more).toBeFocused();
  await expect(page.getByRole("link", { name: "Documents" })).toHaveCount(0);
});
test("mobile More looks active on its routes", async ({ page }) => {
  test.skip((await page.evaluate(() => innerWidth)) >= 768);
  await page.goto("/documents");
  const more = page.getByRole("button", { name: "More" });
  const idle = page.locator(".bottom-nav a").first();
  await expect(more).toHaveAttribute("aria-current", "page");
  await expect
    .poll(() => more.evaluate((el) => getComputedStyle(el).color))
    .not.toBe(await idle.evaluate((el) => getComputedStyle(el).color));
});
test("trailing slash and mixed case show the correct label", async ({ page }) => {
  await page.goto("/DoCuMeNtS/");
  await expect(page.getByRole("heading", { level: 1, name: "Documents" })).toBeVisible();
  await expect(page.locator(".topbar strong")).toHaveText("Documents");
});
