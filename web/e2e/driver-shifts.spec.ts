import { test, expect } from "@playwright/test";
import { driverRoutes, enrolDriver, enterPin } from "./driver-helpers";

test("a complete offline day stays encrypted and survives reload before sync", async ({ page, context }) => {
  await page.clock.setFixedTime(new Date("2026-11-10T08:00:00Z"));
  await driverRoutes(page);
  await page.route("**/api/v1/driver/commands/batch", (route) =>
    route.fulfill({ status: 503, json: { code: "temporarily_unavailable" } }),
  );
  await enrolDriver(page);
  await expect(page.getByRole("button", { name: "Start shift", exact: true })).toBeEnabled();
  await context.setOffline(true);
  await page.getByRole("button", { name: "Start shift", exact: true }).click();
  await page.getByRole("button", { name: "Confirm start", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Other work", exact: true })).toBeVisible();
  await page.clock.setFixedTime(new Date("2026-11-10T09:00:00Z"));
  await page.getByRole("button", { name: "Availability", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Availability", exact: true })).toBeVisible();
  await page.clock.setFixedTime(new Date("2026-11-10T16:00:00Z"));
  await page.getByRole("button", { name: "End shift", exact: true }).click();
  await page.getByRole("button", { name: "Cab", exact: true }).click();
  await expect(page.getByLabel("Country code")).toHaveValue("DE");
  await page.getByRole("button", { name: "Confirm end", exact: true }).click();
  await expect(page.getByText("No shift is running.", { exact: true })).toBeVisible();
  await page.getByRole("link", { name: "Week", exact: true }).click();
  await expect(page.locator(".driver-shift-list")).toContainText("draft");
  await expect(page.locator(".driver-shift-list")).toContainText("8.00 h");
  await expect(page.getByText("DE: 1 night", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Submit week (1 shift)", exact: true }).click();
  await expect(page.locator(".driver-shift-list")).toContainText("submitted");
  await expect(page.locator(".driver-shift-list")).toContainText("Waiting to sync");

  const stored = await page.evaluate(async () => {
    const database = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open("fleetops-driver", 1);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    try {
      return await new Promise<{ keys: string[]; length: number; iv: number }>((resolve, reject) => {
        const request = database.transaction("local").objectStore("local").get("driver-command-queue");
        request.onsuccess = () =>
          resolve({
            keys: Object.keys(request.result).sort(),
            length: request.result.ciphertext.byteLength,
            iv: request.result.iv.length,
          });
        request.onerror = () => reject(request.error);
      });
    } finally {
      database.close();
    }
  });
  expect(stored.keys).toEqual(["ciphertext", "iv"]);
  expect(stored.length).toBeGreaterThan(16);
  expect(stored.iv).toBe(12);

  await context.setOffline(false);
  await page.reload();
  await enterPin(page);
  await expect(page.getByRole("heading", { name: "Week", exact: true })).toBeVisible();
  await expect(page.locator(".driver-shift-list")).toContainText("submitted");
  await expect(page.locator(".driver-shift-list")).toContainText("8.00 h");
  await expect(page.getByText("DE: 1 night", { exact: true })).toBeVisible();
  await page.screenshot({ path: test.info().outputPath("week-offline.png"), fullPage: true });
});
