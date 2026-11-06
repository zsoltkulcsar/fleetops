import { test, expect } from "@playwright/test";

test("gallery redirects and deep links render", async ({ page }) => {
  await page.goto("/__gallery");
  await expect(page).toHaveURL(/\/$/);
  await page.goto("/timesheets");
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await expect(page).toHaveURL(/\/timesheets$/);
});

test("production preview serves the driver app", async ({ request }) => {
  const response = await request.get("/driver/");
  expect(response.ok()).toBeTruthy();
  expect(await response.text()).toContain("<title>FleetOps Driver</title>");
});
