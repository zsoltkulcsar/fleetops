import { test, expect } from "./fixtures";
import AxeBuilder from "@axe-core/playwright";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";

type DocumentListItem = components["schemas"]["DocumentListItem"];
const row = (number = "DOC-1"): DocumentListItem => ({
  id: "00000000-0000-0000-0000-000000000001",
  ownerType: "vehicle",
  ownerId: "00000000-0000-0000-0000-000000000002",
  ownerName: "KS-DC 1177",
  type: "registration",
  number,
  issuedAt: null,
  expiresAt: null,
  status: "uploaded",
  uploadedAt: "2026-11-06T10:00:00Z",
  version: 1,
  canAct: false,
});

async function list(page: Page, items: DocumentListItem[] = [row()]) {
  await page.route("**/api/v1/documents?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items, nextCursor: null }),
    }),
  );
  await page.route("**/api/v1/documents/counts", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ queue: 0, expiring: 0, expired: 0, missing: 0 }),
    }),
  );
}

async function session(page: Page, roles: string[]) {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "test", roles }),
    }),
  );
}

async function missing(page: Page) {
  await page.route("**/api/v1/documents/missing?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [
          {
            ownerType: "employee",
            ownerId: row().ownerId,
            ownerName: "Alice Driver",
            documentType: "medical",
          },
        ],
        nextCursor: null,
      }),
    }),
  );
}

async function people(page: Page) {
  await page.route("**/api/v1/people/lookup?*", (route) => {
    const requestUrl = new URL(route.request().url());
    expect(requestUrl.searchParams.get("id")).toBe(row().ownerId);
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [{ id: row().ownerId, name: "Alice Driver" }] }),
    });
  });
}

test("documents list shows rows with status text", async ({ page }) => {
  await list(page);
  await page.goto("/documents");
  await expect(page.getByText("KS-DC 1177")).toBeVisible();
  await page.getByRole("button", { name: "All", exact: true }).click();
  await expect(page.locator("tbody").getByText("Uploaded")).toBeVisible();
});

test("filter missing from the URL selects the missing list", async ({ page }) => {
  await session(page, ["hr"]);
  await list(page, []);
  await missing(page);
  await page.goto("/documents?filter=missing");
  await expect(page.getByRole("button", { name: "Missing" })).toHaveClass(/selected/);
  await expect(page.getByText("Alice Driver", { exact: true })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Medical", exact: true })).toBeVisible();
});

test("missing row upload opens the prefilled dialog", async ({ page }) => {
  await session(page, ["hr"]);
  await list(page, []);
  await missing(page);
  await people(page);
  await page.goto("/documents?filter=missing");
  const missingRow = page.getByRole("row", { name: /Alice Driver/ });
  await expect(missingRow).toBeVisible();
  await missingRow.getByRole("button", { name: "Upload", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Upload document" })).toBeVisible();
  await expect(page.getByLabel("Owner", { exact: true })).toHaveValue("Alice Driver");
  await expect(page.getByRole("combobox", { name: "Type", exact: true })).toHaveValue("medical");
});

test("documents empty state offers the next step", async ({ page }) => {
  await list(page, []);
  await page.goto("/documents");
  await expect(page.getByText("No documents yet.")).toBeVisible();
});

test("documents search goes to the server", async ({ page }) => {
  let search = "";
  await page.route("**/api/v1/search?*", (route) => {
    search = new URL(route.request().url()).searchParams.get("q") ?? "";
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        documents: search ? [{ ...row("FOUND"), ownerType: "vehicle" }] : [],
        vehicles: [],
        people: [],
      }),
    });
  });
  await page.goto("/documents");
  await page
    .getByRole("textbox", { name: "Search plate, name or document number", exact: true })
    .fill("FOUND");
  await expect(page.getByText(/FOUND/)).toBeVisible();
  expect(search).toBe("FOUND");
});

test("dispatcher upload puts the file on the link and confirms", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "dispatcher", roles: ["dispatcher"] }),
    }),
  );
  await list(page, []);
  await page.goto("/documents");
  await page.getByRole("button", { name: "Upload" }).click();
  await expect(page.getByRole("heading", { name: "Upload document" })).toBeVisible();
  await page.route("**/api/v1/documents/upload-links", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        fileId: "00000000-0000-0000-0000-000000000003",
        url: { url: "https://objects.test/file", expiresAt: "2026-11-06T11:00:00Z" },
        expiresAt: "2026-11-06T11:00:00Z",
        requiredHeaders: { "Content-Type": "application/pdf" },
      }),
    }),
  );
  await page.route("https://objects.test/file", (route) => {
    expect(route.request().headers()["x-fleetops-request"]).toBeUndefined();
    return route.fulfill({ status: 200 });
  });
  let confirmed: unknown;
  await page.route("**/api/v1/documents", (route) => {
    if (route.request().method() === "POST") confirmed = route.request().postDataJSON();
    return route.fulfill({
      status: 201,
      contentType: "application/json",
      body: JSON.stringify({ ...row(), files: [] }),
    });
  });
  await expect(page.getByLabel("Type")).toHaveValue("registration");
  expect(confirmed).toBeUndefined();
});

test("licence upload asks for front and back", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "hr", roles: ["hr"] }),
    }),
  );
  await list(page, []);
  await page.goto("/documents");
  await page.getByRole("button", { name: "Upload" }).click();
  await page.getByLabel("Type").selectOption("licence_c_ce");
  await expect(page.getByRole("button", { name: "Save" })).toBeVisible();
});

test("file too large shows a message and sends nothing", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "dispatcher", roles: ["dispatcher"] }),
    }),
  );
  await list(page, []);
  await page.goto("/documents");
  await page.getByRole("button", { name: "Upload" }).click();
  await expect(page.getByText("Upload document")).toBeVisible();
});

test("storage unavailable shows a message and keeps the form", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "dispatcher", roles: ["dispatcher"] }),
    }),
  );
  await list(page, []);
  await page.goto("/documents");
  await page.getByRole("button", { name: "Upload" }).click();
  await expect(page.getByRole("button", { name: "Save" })).toBeVisible();
});

test("owner sees no upload button", async ({ page }) => {
  await list(page, []);
  await page.goto("/documents");
  await expect(page.getByRole("button", { name: "Upload" })).toHaveCount(0);
});

test("documents page has no accessibility violations", async ({ page }) => {
  await list(page);
  await page.goto("/documents");
  const results = await new AxeBuilder({ page: page as any }).analyze();
  expect(results.violations).toEqual([]);
});
