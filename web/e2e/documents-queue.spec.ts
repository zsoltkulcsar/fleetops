import AxeBuilder from "@axe-core/playwright";
import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";

type DocumentListItem = components["schemas"]["DocumentListItem"];
type DocumentCounts = components["schemas"]["DocumentCounts"];

const row = (overrides: Partial<DocumentListItem> = {}): DocumentListItem => ({
  id: "00000000-0000-0000-0000-000000000001",
  ownerType: "vehicle",
  ownerId: "00000000-0000-0000-0000-000000000002",
  ownerName: "KS-DC 1177",
  type: "registration",
  number: "DOC-1",
  issuedAt: null,
  expiresAt: null,
  status: "uploaded",
  uploadedAt: "2026-11-06T10:00:00Z",
  version: 1,
  canAct: true,
  ...overrides,
});

async function session(page: Page, roles: string[]) {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "test", roles }),
    }),
  );
}

async function queue(
  page: Page,
  items: DocumentListItem[],
  counts: DocumentCounts = { queue: items.length, expiring: 0, expired: 0, missing: 0 },
) {
  await page.route("**/api/v1/documents?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items, nextCursor: null }),
    }),
  );
  await page.route("**/api/v1/documents/counts", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(counts) }),
  );
}

test("queue shows a card with verify and reject", async ({ page }) => {
  await session(page, ["hr"]);
  await queue(page, [row({ ownerType: "employee", ownerName: "Alice Driver", type: "code95" })]);
  await page.goto("/documents");
  await expect(page.getByText("Alice Driver")).toBeVisible();
  await expect(page.getByRole("button", { name: "Verify", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Reject" })).toBeVisible();
});

test("verify sends the expiry date and clears the card", async ({ page }) => {
  await session(page, ["hr"]);
  let cleared = false;
  await page.route("**/api/v1/documents?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: cleared ? [] : [row({ type: "code95" })], nextCursor: null }),
    }),
  );
  await page.route("**/api/v1/documents/counts", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ queue: cleared ? 0 : 1, expiring: 0, expired: 0, missing: 0 }),
    }),
  );
  let body: unknown;
  await page.route("**/api/v1/documents/*/verify", (route) => {
    cleared = true;
    body = route.request().postDataJSON();
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ...row(), status: "verified", canAct: false }),
    });
  });
  await page.goto("/documents");
  await page.getByLabel("Expires").fill("2027-01-10");
  await page.getByRole("button", { name: "Verify", exact: true }).click();
  await expect.poll(() => body).toMatchObject({ expiresAt: "2027-01-10" });
  await expect(page.getByRole("button", { name: "Verify", exact: true })).toHaveCount(0);
  await expect(
    page.getByText("No documents yet. Start with driver licences and vehicle registrations.", {
      exact: true,
    }),
  ).toBeVisible();
});

test("reject needs a reason", async ({ page }) => {
  await session(page, ["hr"]);
  await queue(page, [row()]);
  await page.goto("/documents");
  await expect(page.getByRole("button", { name: "Reject" })).toBeDisabled();
  await page.getByLabel("Reason", { exact: true }).fill("Unreadable");
  await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();
});

test("hr sees delete misupload and a dispatcher does not", async ({ page }) => {
  await session(page, ["hr"]);
  await queue(page, [row()]);
  await page.goto("/documents");
  await expect(page.getByRole("button", { name: "Delete misupload" })).toBeVisible();
  await session(page, ["dispatcher"]);
  await page.reload();
  await expect(page.getByRole("button", { name: "Delete misupload" })).toHaveCount(0);
});

test("read only card explains who verifies", async ({ page }) => {
  await session(page, ["owner"]);
  await queue(page, [row({ ownerType: "employee", canAct: false })]);
  await page.goto("/documents");
  await expect(page.getByText("Only HR can verify employee documents.", { exact: true })).toBeVisible();
});

test("documents nav badge shows the queue count", async ({ page }) => {
  await session(page, ["hr"]);
  await queue(page, [row()], { queue: 3, expiring: 0, expired: 0, missing: 0 });
  await page.goto("/documents");
  await expect(page.getByText("Documents").first()).toBeVisible();
  await expect(page.getByText("3").first()).toBeVisible();
});

test("sight check is offered on a verified licence", async ({ page }) => {
  await session(page, ["hr"]);
  await queue(page, []);
  await page.route("**/api/v1/documents/00000000-0000-0000-0000-000000000001", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        ...row({ type: "licence_c_ce", status: "verified", canAct: false }),
        files: [],
      }),
    }),
  );
  await page.goto("/documents?open=00000000-0000-0000-0000-000000000001");
  await expect(page.getByText("Record sight check")).toBeVisible();
});

test("global search shows no results for an unknown document", async ({ page }) => {
  await session(page, ["hr"]);
  await queue(page, []);
  await page.route("**/api/v1/search?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ documents: [], vehicles: [], people: [] }),
    }),
  );
  await page.goto("/documents");
  await page
    .getByRole("textbox", { name: "Search plate, name or document number", exact: true })
    .fill("DOC-1");
  await expect(page.getByText("No results.")).toBeVisible();
});

test("slash focuses the global search", async ({ page }) => {
  await queue(page, []);
  await page.goto("/documents");
  const search = page.getByRole("textbox", {
    name: "Search plate, name or document number",
    exact: true,
  });
  await expect(search).toBeVisible();
  await page.keyboard.press("/");
  await expect(search).toBeFocused();
});

test("queue has no accessibility violations", async ({ page }) => {
  await session(page, ["hr"]);
  await queue(page, [row()]);
  await page.goto("/documents");
  const results = await new AxeBuilder({ page: page as any }).analyze();
  expect(results.violations).toEqual([]);
});
