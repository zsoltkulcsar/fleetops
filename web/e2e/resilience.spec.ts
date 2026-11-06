import { test, expect } from "./fixtures";

test("a render error shows the banner with Try again and keeps navigation", async ({ page }) => {
  await page.goto("/__gallery");
  await page.getByRole("button", { name: "Trigger render failure" }).click();
  await expect(page.getByRole("alert")).toContainText("This section could not be displayed. Try again.");
  await expect(page.getByRole("button", { name: "Try again" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Fleet", exact: true })).toBeVisible();
});
test("the error boundary resets on route change", async ({ page }) => {
  await page.goto("/__gallery");
  await page.getByRole("button", { name: "Trigger render failure" }).click();
  await page.getByRole("link", { name: "Fleet", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Fleet" })).toBeVisible();
  await expect(page.getByRole("alert")).toHaveCount(0);
});
