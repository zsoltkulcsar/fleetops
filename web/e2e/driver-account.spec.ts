import { test, expect } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import { config, driverRoutes, enrolDriver, storedVault, enterPin, pin } from "./driver-helpers";
import { peopleProblem } from "./people-helpers";

test.beforeEach(async ({ page }) => {
  await driverRoutes(page);
});

test("suspended driver sees account suspended and no start", async ({ page }) => {
  const suspended: components["schemas"]["DriverConfig"] = {
    ...config,
    employeeStatus: "suspended",
    pinResetRequired: false,
  };
  await page.route("**/api/v1/driver/config", (route) => route.fulfill({ json: suspended }));
  await enrolDriver(page);
  await expect(page.getByRole("alert").filter({ hasText: "Account suspended. Contact HR" })).toBeVisible();
  await expect(page.getByRole("button", { name: /start/i })).toHaveCount(0);
  await page.getByRole("link", { name: "Me", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Your documents", exact: true })).toBeVisible();
});

test("onboarding driver sees not active yet", async ({ page }) => {
  const onboarding: components["schemas"]["DriverConfig"] = {
    ...config,
    employeeStatus: "onboarding",
    pinResetRequired: false,
  };
  await page.route("**/api/v1/driver/config", (route) => route.fulfill({ json: onboarding }));
  await enrolDriver(page);
  await expect(page.getByRole("alert").filter({ hasText: "Not active yet. Contact HR" })).toBeVisible();
  await expect(page.getByRole("button", { name: /start/i })).toHaveCount(0);
});

test("revoked phone lists unsent items then returns to enrolment", async ({ page }) => {
  await enrolDriver(page);
  await expect(page.getByRole("button", { name: "Start shift", exact: true })).toBeEnabled();
  // Queue an actual driver command through the UI while offline.
  await page.context().setOffline(true);
  await page.getByRole("button", { name: "Start shift", exact: true }).click();
  await page.getByRole("button", { name: "Confirm start", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Other work", exact: true })).toBeVisible();
  const problem = peopleProblem("session_revoked");
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ status: 401, json: problem }));
  const unavailable: components["schemas"]["ProblemDetails"] = { status: 503 };
  await page.route("**/api/v1/driver/commands/batch", (route) =>
    route.fulfill({ status: 503, json: unavailable }),
  );
  await page.context().setOffline(false);
  await expect(page.getByRole("heading", { name: "This phone was signed out. Not sent:" })).toBeVisible();
  await expect(page.getByRole("listitem").filter({ hasText: /shift start/ })).toBeVisible();
  expect((await storedVault(page))?.wrappedKey).toBeDefined();
  await page.getByRole("button", { name: "OK", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Set up your driver app" })).toBeVisible();
  expect(await storedVault(page)).toBeUndefined();
  await page.reload();
  await expect(page.getByRole("heading", { name: "Set up your driver app" })).toBeVisible();
});

test("pin reset asks for a new pin twice", async ({ page }) => {
  await enrolDriver(page);
  const reset: components["schemas"]["DriverConfig"] = { ...config, pinResetRequired: true };
  await page.route("**/api/v1/driver/config", (route) => route.fulfill({ json: reset }));
  let completions = 0;
  await page.route("**/api/v1/driver/pin-reset/complete", (route) => {
    completions += 1;
    reset.pinResetRequired = false;
    const result: components["schemas"]["AccessActionResult"] = { code: "pin_reset_completed" };
    return route.fulfill({ json: result });
  });
  await page.reload();
  await expect(page.getByLabel("PIN", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Confirm PIN", { exact: true })).toBeVisible();
  await page.getByLabel("PIN", { exact: true }).fill("826493");
  await page.getByLabel("Confirm PIN", { exact: true }).fill("826492");
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText(/match/i);
  expect(completions).toBe(0);
  await page.getByLabel("Confirm PIN", { exact: true }).fill("826493");
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  await expect(page.getByRole("navigation", { name: "Driver navigation" })).toBeVisible();
  expect(completions).toBe(1);
  await page.reload();
  await expect(page.getByRole("heading", { name: "Enter your PIN" })).toBeVisible();
  await enterPin(page, pin);
  await expect(page.getByRole("alert")).toHaveText("Wrong PIN. 4 tries left.");
  await enterPin(page, "826493");
  await expect(page.getByRole("navigation", { name: "Driver navigation" })).toBeVisible();
});
