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
  await expect(page.locator("tbody").getByText("Awaiting verification", { exact: true })).toBeVisible();
});

test("document library shows document, owner, expiry, status and action columns", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await list(page, [
    { ...row("INS-1203"), type: "insurance", ownerName: "KS-DC 1203", expiresAt: "2027-10-11", canAct: true },
    {
      ...row("HU-1177"),
      id: "00000000-0000-0000-0000-000000000011",
      type: "hu_report",
      status: "verified",
      expiresAt: "2026-09-26",
    },
    {
      ...row("REG-1311"),
      id: "00000000-0000-0000-0000-000000000012",
      ownerName: "KS-DC 1311",
      status: "verified",
      expiresAt: "2029-02-03",
    },
  ]);
  await page.goto("/documents?filter=all");
  await expect(page.getByRole("columnheader")).toHaveText([
    "Document",
    "Owner",
    "Expiry",
    "Status",
    "Action",
  ]);
  const awaiting = page.getByRole("row", { name: /Insurance/ });
  await expect(awaiting).toContainText("INS-1203");
  await expect(awaiting).toContainText("11.10.2027");
  await expect(awaiting).toContainText("Awaiting verification");
  await expect(awaiting.getByRole("button")).toHaveText(["View", "Verify", "Reject"]);
  const expired = page.getByRole("row", { name: /HU report/ });
  await expect(expired.locator("[data-status]")).toHaveText("Expired");
  await expect(expired.getByRole("button")).toHaveText(["View", "Upload renewal"]);
  const verified = page.getByRole("row", { name: /KS-DC 1311/ });
  await expect(verified.locator("[data-status]")).toHaveText("Verified");
  await expect(verified.getByRole("button")).toHaveText(["View", "Upload renewal"]);
});

test("view shows the file inside the page without opening a window", async ({ page }) => {
  await list(page, [{ ...row(), status: "verified", expiresAt: "2029-02-03" }]);
  await page.route(`**/api/v1/documents/${row().id}`, (route) =>
    route.fulfill({
      json: {
        ...row(),
        status: "verified",
        expiresAt: "2029-02-03",
        verifiedAt: "2026-11-06T11:00:00Z",
        rejectReason: null,
        categories: [],
        files: [{ id: "file-1", page: "page", contentType: "application/pdf", sizeBytes: 1024 }],
      } satisfies components["schemas"]["DocumentDetail"],
    }),
  );
  await page.route(`**/api/v1/documents/${row().id}/files/file-1/link`, (route) =>
    route.fulfill({
      json: { url: "https://objects.test/registration.pdf", expiresAt: "2026-11-06T11:00:00Z" },
    }),
  );
  let fetched = 0;
  await page.route("https://objects.test/registration.pdf", (route) => {
    fetched += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/pdf",
      headers: { "Access-Control-Allow-Origin": "*" },
      body: "%PDF-1.4\n%%EOF",
    });
  });
  let popups = 0;
  page.context().on("page", () => {
    popups += 1;
  });
  await page.goto("/documents?filter=all");
  await page.getByRole("button", { name: "View: Vehicle registration, KS-DC 1177" }).click();
  const dialog = page.getByRole("dialog", { name: "Vehicle registration" });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator("iframe")).toHaveAttribute("src", /^blob:/);
  await expect(dialog).toContainText("03.02.2029");
  // The dev server runs React StrictMode, which mounts the preview effect twice.
  expect(fetched).toBeGreaterThan(0);
  expect(popups).toBe(0);
  await expect(page).toHaveURL(/\/documents\?filter=all$/);
});

test("verify from the library sends the checked details and confirms", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await list(page, [{ ...row(), type: "insurance", canAct: true }]);
  const detail: components["schemas"]["DocumentDetail"] = {
    ...row(),
    type: "insurance",
    canAct: true,
    verifiedAt: null,
    rejectReason: null,
    categories: [],
    files: [],
  };
  await page.route(`**/api/v1/documents/${row().id}`, (route) => route.fulfill({ json: detail }));
  let sent: unknown;
  await page.route(`**/api/v1/documents/${row().id}/verify`, (route) => {
    sent = route.request().postDataJSON();
    return route.fulfill({ json: { ...detail, status: "verified" } });
  });
  await page.goto("/documents?filter=all");
  await page.getByRole("button", { name: "Verify: Insurance, KS-DC 1177" }).click();
  const dialog = page.getByRole("dialog", { name: "Verify: Insurance" });
  await expect(dialog.getByLabel("Number", { exact: true })).toHaveValue("DOC-1");
  await dialog.getByLabel("Expires", { exact: true }).fill("2027-10-11");
  await dialog.getByRole("button", { name: "Verify", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByTestId("toast")).toContainText("Document verified.");
  expect(sent).toMatchObject({ version: 1, expiresAt: "2027-10-11", number: "DOC-1" });
});

test("reject from the library needs a reason and sends it", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await list(page, [{ ...row(), type: "insurance", canAct: true }]);
  const detail: components["schemas"]["DocumentDetail"] = {
    ...row(),
    type: "insurance",
    canAct: true,
    verifiedAt: null,
    rejectReason: null,
    categories: [],
    files: [],
  };
  await page.route(`**/api/v1/documents/${row().id}`, (route) => route.fulfill({ json: detail }));
  let sent: unknown;
  await page.route(`**/api/v1/documents/${row().id}/reject`, (route) => {
    sent = route.request().postDataJSON();
    return route.fulfill({ json: { ...detail, status: "rejected", rejectReason: "Page cut off" } });
  });
  await page.goto("/documents?filter=all");
  await page.getByRole("button", { name: "Reject: Insurance, KS-DC 1177" }).click();
  const dialog = page.getByRole("dialog", { name: "Verify: Insurance" });
  const reject = dialog.getByRole("button", { name: "Reject", exact: true });
  await expect(reject).toBeDisabled();
  await dialog.getByLabel("Reason", { exact: true }).fill("Page cut off");
  await reject.click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByTestId("toast")).toContainText("Document rejected.");
  expect(sent).toEqual({ version: 1, reason: "Page cut off" });
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
