import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";

type NotificationList = components["schemas"]["NotificationListResult"];
type NotificationItem = components["schemas"]["NotificationItem"];

const id = "00000000-0000-0000-0000-000000000027";

const item = (overrides: Partial<NotificationItem> = {}): NotificationItem => ({
  id,
  templateCode: "document_rejected",
  text: "Driver card document was rejected",
  createdAt: "2026-11-07T09:05:00Z",
  readAt: null,
  link: "/documents?open=00000000-0000-0000-0000-000000000099",
  ...overrides,
});

const list = (overrides: Partial<NotificationList> = {}): NotificationList => ({
  items: [item()],
  unread: 1,
  nextCursor: null,
  ...overrides,
});

async function notifications(page: Page, value: NotificationList) {
  await page.route("**/api/v1/notifications?*", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(value) }),
  );
}

async function openBell(page: Page) {
  await page.goto("/fleet");
  await page.getByRole("button", { name: /^\d+ unread notifications?$/ }).click();
  await expect(page.getByRole("dialog", { name: "Notifications" })).toBeVisible();
}

test("bell shows the unread count as text", async ({ page }) => {
  await notifications(page, list({ unread: 3 }));
  await page.goto("/fleet");

  await expect(page.getByRole("button", { name: "3 unread notifications" })).toContainText("3");
});

test("bell label is singular for one unread", async ({ page }) => {
  await notifications(page, list({ unread: 1 }));
  await page.goto("/fleet");

  await expect(page.getByRole("button", { name: "1 unread notification" })).toBeVisible();
});

test("bell lists notifications with dates", async ({ page }) => {
  await notifications(
    page,
    list({
      items: [item(), item({ id: "00000000-0000-0000-0000-000000000028", readAt: "2026-11-07T09:10:00Z" })],
    }),
  );
  await openBell(page);

  await expect(page.getByText("Driver card document was rejected")).toHaveCount(2);
  await expect(page.getByText("07.11.2026 10:05", { exact: true })).toBeVisible();
});

test("row click marks read and opens the link", async ({ page }) => {
  await notifications(page, list());
  let markedRead = false;
  await page.route(`**/api/v1/notifications/${id}/read`, (route) => {
    markedRead = true;
    route.fulfill({ status: 200, body: "" });
  });
  await openBell(page);
  await page.getByRole("button", { name: /Driver card document was rejected/ }).click();

  expect(markedRead).toBe(true);
  await expect(page).toHaveURL(/\/documents\?open=/);
});

test("read and read all send the request header", async ({ page }) => {
  await notifications(page, list());
  let readHeader: string | undefined;
  let readAllHeader: string | undefined;
  await page.route(`**/api/v1/notifications/${id}/read`, async (route) => {
    readHeader = route.request().headers()["x-fleetops-request"];
    await route.fulfill({ status: 200, body: "" });
  });
  await page.route("**/api/v1/notifications/read-all", async (route) => {
    readAllHeader = route.request().headers()["x-fleetops-request"];
    await route.fulfill({ status: 200, body: "1" });
  });

  await openBell(page);
  await page.getByRole("button", { name: /Driver card document was rejected/ }).click();
  await expect(page).toHaveURL(/\/documents\?open=/);

  await openBell(page);
  await page.getByRole("button", { name: "Mark all as read" }).click();

  expect(readHeader).toBe("1");
  expect(readAllHeader).toBe("1");
});

test("mark all as read clears the count", async ({ page }) => {
  let unread = 2;
  await page.route("**/api/v1/notifications?*", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(list({ unread })) }),
  );
  await page.route("**/api/v1/notifications/read-all", async (route) => {
    unread = 0;
    await route.fulfill({ status: 200, body: "2" });
  });
  await openBell(page);
  await page.getByRole("button", { name: "Mark all as read" }).click();

  await expect(page.getByRole("button", { name: "0 unread notifications" })).toBeVisible();
});

test("empty bell says so", async ({ page }) => {
  await notifications(page, list({ items: [], unread: 0 }));
  await openBell(page);

  await expect(page.getByText("No notifications.")).toBeVisible();
});

test("escape closes the bell", async ({ page }) => {
  await notifications(page, list());
  await openBell(page);
  await page.keyboard.press("Escape");

  await expect(page.getByRole("dialog", { name: "Notifications" })).toBeHidden();
});

test("bell has no accessibility violations", async ({ page }) => {
  await notifications(page, list());
  await openBell(page);
  const results = await new AxeBuilder({ page: page as never }).include(".notification-popover").analyze();

  expect(results.violations).toEqual([]);
});
