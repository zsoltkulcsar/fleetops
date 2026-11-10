import { test, expect } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import { driverRoutes, enrolDriver, enterPin, storedVault, config } from "./driver-helpers";

test.beforeEach(async ({ page }) => {
  await driverRoutes(page);
  await enrolDriver(page);
  await page.reload();
  await expect(page.getByRole("heading", { name: "Enter your PIN" })).toBeVisible();
});

test("right pin opens the shell", async ({ page }) => {
  await enterPin(page);
  await expect(page.getByRole("heading", { name: "Shift", exact: true })).toBeVisible();
  await expect(page.getByText("Hello, Dana.", { exact: true })).toBeVisible();
});

test("pin works with no signal", async ({ page, context }) => {
  await context.setOffline(true);
  await enterPin(page);
  await expect(page.getByRole("heading", { name: "Shift", exact: true })).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: /^Offline$/ })).toBeVisible();
});

test("wrong pin shows tries left", async ({ page }) => {
  await enterPin(page, "111111");
  await expect(page.getByRole("alert")).toHaveText("Wrong PIN. 4 tries left.");
  expect((await storedVault(page))?.failedTries).toBe(1);
  await expect(page.getByRole("navigation")).toHaveCount(0);
});

async function exhaustTries(page: import("@playwright/test").Page) {
  for (let attempt = 1; attempt <= 5; attempt += 1) {
    await enterPin(page, "111111");
    if (attempt < 5) {
      const remaining = 5 - attempt;
      await expect(page.getByRole("alert")).toHaveText(
        `Wrong PIN. ${remaining} ${remaining === 1 ? "try" : "tries"} left.`,
      );
    }
  }
  await expect(page.getByRole("heading", { name: "Too many wrong tries. Call the office." })).toBeVisible();
}

test("five wrong pins lock with call the office", async ({ page }) => {
  await exhaustTries(page);
  await expect(page.getByRole("link", { name: "Call the office", exact: true })).toHaveAttribute(
    "href",
    `tel:${config.officePhone}`,
  );
  await expect(page.getByRole("button", { name: "Unlock", exact: true })).toHaveCount(0);
  const vault = await storedVault(page);
  expect(vault?.locked).toBe(true);
  expect(vault?.failedTries).toBe(5);
  expect(vault?.wrappedKey).toBeUndefined();
});

test("locked stays locked after reload", async ({ page }) => {
  await exhaustTries(page);
  await page.reload();
  await expect(page.getByRole("heading", { name: "Too many wrong tries. Call the office." })).toBeVisible();
  await expect(page.getByLabel("PIN", { exact: true })).toHaveCount(0);
  expect((await storedVault(page))?.locked).toBe(true);
});

test("reload does not reset the try count", async ({ page }) => {
  await enterPin(page, "111111");
  await expect(page.getByRole("alert")).toHaveText("Wrong PIN. 4 tries left.");
  await page.reload();
  await enterPin(page, "111111");
  await expect(page.getByRole("alert")).toHaveText("Wrong PIN. 3 tries left.");
  expect((await storedVault(page))?.failedTries).toBe(2);
});

test("old vault asks for internet", async ({ page, context }) => {
  const vault = await storedVault(page);
  expect(vault).toBeDefined();
  await context.setOffline(true);
  await page.clock.setFixedTime(new Date(vault!.lastOnlineAt + (config.maxOfflineDays + 1) * 86_400_000));
  // Unlock reads the real persisted timestamp against the advanced wall clock.
  await enterPin(page);
  await expect(page.getByRole("heading", { name: "Connect to the internet to go on." })).toBeVisible();
  await expect(page.getByRole("button", { name: "Try again" })).toBeDisabled();
  await expect(page.getByRole("navigation")).toHaveCount(0);
  expect((await storedVault(page))?.wrappedKey).toBeDefined();
});

test("revoked session erases the vault", async ({ page }) => {
  const problem: components["schemas"]["ProblemDetails"] = { status: 401 };
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ status: 401, json: problem }));
  expect((await storedVault(page))?.wrappedKey).toBeDefined();
  await enterPin(page);
  await expect(
    page.getByRole("heading", { name: "Your access was removed. Call the office." }),
  ).toBeVisible();
  expect(await storedVault(page)).toBeUndefined();
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Your access was removed. Call the office." }),
  ).toBeVisible();
  expect(await storedVault(page)).toBeUndefined();
});

test("network failure does not erase the vault", async ({ page }) => {
  let failures = 0;
  await page.route("**/api/v1/auth/session", async (route) => {
    failures += 1;
    await route.abort("failed");
  });
  await enterPin(page);
  await expect(page.getByRole("navigation", { name: "Driver navigation" })).toBeVisible();
  expect(failures).toBeGreaterThan(0);
  expect((await storedVault(page))?.wrappedKey).toBeDefined();
  await page.reload();
  await enterPin(page);
  await expect(page.getByRole("navigation", { name: "Driver navigation" })).toBeVisible();
  expect((await storedVault(page))?.wrappedKey).toBeDefined();
});
