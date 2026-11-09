import { expect, type Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";

export const pin = "739126";
export const config: components["schemas"]["DriverConfig"] = {
  pinLength: 6,
  maxPinTries: 5,
  maxOfflineDays: 7,
  officePhone: "+40123456789",
  name: "Dana Driver",
};

export async function driverRoutes(page: Page) {
  const enrolled: components["schemas"]["DriverEnrolmentResponse"] = { stage: "pending_privacy" };
  await page.route("**/api/v1/auth/driver/enrol", (route) => route.fulfill({ json: enrolled }));
  await page.route("**/api/v1/auth/privacy-notice", (route) => route.fulfill({ status: 200 }));
  await page.route("**/api/v1/driver/config", (route) => route.fulfill({ json: config }));
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ status: 200 }));
  const snapshot: components["schemas"]["DriverSnapshot"] = {
    employeeId: "00000000-0000-0000-0000-000000000031",
    generatedAt: "2026-11-09T10:00:00Z",
    cycles: [],
    documents: [],
    canDrive: true,
    readinessCauses: [],
  };
  await page.route("**/api/v1/driver/snapshot", (route) => route.fulfill({ json: snapshot }));
  const notifications: components["schemas"]["NotificationListResult"] = {
    items: [],
    unread: 0,
    nextCursor: null,
  };
  await page.route("**/api/v1/notifications?*", (route) => route.fulfill({ json: notifications }));
}

export async function openEnrol(page: Page) {
  await page.goto("/driver/");
  await page.getByRole("button", { name: "Not now", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Set up your driver app" })).toBeVisible();
}

export async function submitEnrol(page: Page) {
  await page.getByLabel("Personnel number", { exact: true }).fill("D-100");
  await page.getByLabel("SMS code", { exact: true }).fill("123456");
  await page.getByRole("button", { name: "Continue", exact: true }).click();
}

export async function reachPin(page: Page) {
  await openEnrol(page);
  await submitEnrol(page);
  await page.getByRole("button", { name: "I acknowledge the privacy notice" }).click();
  await expect(page.getByLabel("Confirm PIN", { exact: true })).toBeVisible();
}

export async function setPin(page: Page) {
  await page.getByLabel("PIN", { exact: true }).fill(pin);
  await page.getByLabel("Confirm PIN", { exact: true }).fill(pin);
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  await expect(page.getByRole("navigation", { name: "Driver navigation" })).toBeVisible();
}

export async function enrolDriver(page: Page) {
  await reachPin(page);
  await setPin(page);
}

export async function enterPin(page: Page, value = pin) {
  await page.getByLabel("PIN", { exact: true }).fill(value);
  await page.getByRole("button", { name: "Unlock", exact: true }).click();
}

// Read the persisted record independently of the application's in-memory state.
export async function storedVault(page: Page) {
  return page.evaluate(async () => {
    const database = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open("fleetops-driver", 1);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    try {
      return await new Promise<
        { failedTries: number; locked: boolean; wrappedKey?: ArrayBuffer; lastOnlineAt: number } | undefined
      >((resolve, reject) => {
        const request = database.transaction("local").objectStore("local").get("pin-vault");
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });
    } finally {
      database.close();
    }
  });
}
