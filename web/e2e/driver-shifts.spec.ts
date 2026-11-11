import { test, expect } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import type { Page } from "@playwright/test";
import { shift, history } from "./timesheets-helpers";
import { driverRoutes, enrolDriver, enterPin } from "./driver-helpers";

async function driverWeek(page: Page, current: components["schemas"]["ShiftView"]) {
  await page.clock.setFixedTime(new Date("2026-11-10T10:00:00Z"));
  await driverRoutes(page);
  const snapshot: components["schemas"]["ShiftSnapshot"] = {
    generatedAt: "2026-11-10T10:00:00Z",
    companyTimeZone: "Europe/Berlin",
    shifts: [current],
    appliedCommandIds: [],
    trucks: [],
    suggestedTruckId: null,
    conflicts: [],
    assignments: [],
  };
  await page.route("**/api/v1/driver/shifts", (route) => route.fulfill({ json: snapshot }));
  await enrolDriver(page);
  await page.getByRole("link", { name: "Week", exact: true }).click();
  await expect(page.locator(".driver-shift-list li")).toHaveCount(1);
}

test("driver sees what the office changed on a shift", async ({ page }) => {
  const current: components["schemas"]["ShiftView"] = {
    id: shift.id,
    date: shift.date,
    startAt: shift.startAt,
    endAt: shift.endAt,
    truckId: null,
    trailerId: null,
    status: "submitted",
    version: 3,
    rejectReason: null,
    blockedAtStart: null,
    deviceTimeGap: false,
    activities: shift.activities,
    overnights: shift.overnights,
    officeChanges: [history],
  };
  await driverWeek(page, current);
  await expect(page.getByText("Changed by the office. Open for differences.", { exact: true })).toBeVisible();
  await page.locator(".driver-shift-list button").click();
  const changes = page.getByRole("region", { name: "Office changes" });
  await expect(changes.getByRole("heading", { name: "Changed by the office", exact: true })).toBeVisible();
  await expect(changes).toContainText("Helen Office");
  await expect(changes).toContainText("Corrected end from work record");
  await expect(changes).toContainText("Shift end: 09.11.2026 16:00 \u2192 09.11.2026 17:00");
});

test("driver change list shows plain labels and no ids", async ({ page }) => {
  const current: components["schemas"]["ShiftView"] = {
    ...shift,
    status: "rejected",
    blockedAtStart: null,
    deviceTimeGap: false,
    officeChanges: [
      { ...history, field: "Status", before: "submitted", after: "rejected" },
      {
        ...history,
        auditId: "00000000-0000-0000-0000-000000000065",
        field: "TruckId",
        before: "B-OLD 10",
        after: "B-NEW 20",
      },
    ],
  };
  await driverWeek(page, current);
  await page.locator(".driver-shift-list button").click();
  const changes = page.getByRole("region", { name: "Office changes" });
  await expect(changes).toContainText("Status: Awaiting approval \u2192 Rejected");
  await expect(changes).toContainText("Truck: B-OLD 10 \u2192 B-NEW 20");
  await expect(changes).not.toContainText(
    /(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i,
  );
});

test("a rejected shift shows the reason with fix and resubmit", async ({ page, context }) => {
  const current: components["schemas"]["ShiftView"] = {
    id: shift.id,
    date: shift.date,
    startAt: shift.startAt,
    endAt: shift.endAt,
    truckId: null,
    trailerId: null,
    status: "rejected",
    version: 3,
    rejectReason: "Correct the activity type",
    blockedAtStart: null,
    deviceTimeGap: false,
    activities: shift.activities,
    overnights: shift.overnights,
  };
  await driverWeek(page, current);
  await context.setOffline(true);
  await expect(
    page.getByText("Rejected: Correct the activity type · Fix and resubmit", { exact: true }),
  ).toBeVisible();
  await page.locator(".driver-shift-list button").click();
  const drawer = page.getByRole("dialog");
  await expect(
    drawer.getByText("Correct the activity type. Fix and resubmit.", { exact: true }),
  ).toBeVisible();
  await drawer.getByRole("button", { name: "Edit activity", exact: true }).click();
  await drawer.getByLabel("Activity", { exact: true }).selectOption("availability");
  await drawer.getByRole("button", { name: "Save activity", exact: true }).click();
  await expect(drawer.getByText("Availability", { exact: true }).filter({ visible: true })).toBeVisible();
  await drawer.getByRole("button", { name: "Resubmit shift", exact: true }).click();
  await expect(drawer).toContainText("Awaiting approval");
  await expect(drawer.getByRole("button", { name: "Resubmit shift", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Close shift", exact: true }).click();
  await expect(page.locator(".driver-shift-list")).toContainText("Awaiting approval");
  await expect(page.locator(".driver-shift-list")).toContainText("Waiting to sync");
});

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
  await expect(page.locator(".driver-shift-list")).toContainText("Draft");
  await expect(page.locator(".driver-shift-list")).toContainText("8.00 h");
  await expect(page.getByText("DE: 1 night", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Submit week (1 shift)", exact: true }).click();
  await expect(page.locator(".driver-shift-list")).toContainText("Awaiting approval");
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
  await expect(page.locator(".driver-shift-list")).toContainText("Awaiting approval");
  await expect(page.locator(".driver-shift-list")).toContainText("8.00 h");
  await expect(page.getByText("DE: 1 night", { exact: true })).toBeVisible();
  await page.screenshot({ path: test.info().outputPath("week-offline.png"), fullPage: true });
});
