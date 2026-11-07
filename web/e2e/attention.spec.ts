import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";

type Attention = components["schemas"]["AttentionResult"];
type AttentionGroup = components["schemas"]["AttentionGroup"];
type AttentionItem = components["schemas"]["AttentionItem"];
type AlertActionResult = components["schemas"]["AlertActionResult"];

const ids = (number: number) => `00000000-0000-0000-0000-${number.toString().padStart(12, "0")}`;

const item = (overrides: Partial<AttentionItem> = {}): AttentionItem => ({
  id: ids(1),
  ruleId: "A-03",
  severity: "warning",
  scopeKey: "registration",
  dueAt: "2026-11-20",
  status: "open",
  snoozable: true,
  version: 1,
  ...overrides,
});

const group = (overrides: Partial<AttentionGroup> = {}): AttentionGroup => ({
  subjectType: "vehicle",
  subjectId: ids(2),
  subjectName: "B-X 1001",
  worstSeverity: "warning",
  items: [item()],
  ...overrides,
});

const attention = (groups: AttentionGroup[], overrides: Partial<Attention> = {}): Attention => ({
  groups,
  totals: { open: groups.reduce((total, current) => total + current.items.length, 0), critical: 0 },
  companyEmpty: false,
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

async function queue(page: Page, value: Attention) {
  await page.route("**/api/v1/attention", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(value) }),
  );
  await page.route("**/api/v1/readiness/summary", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ vehiclesUsable: 1, vehiclesTotal: 1 }),
    }),
  );
}

test("critical group renders as a full card first", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await queue(
    page,
    attention(
      [
        group({ worstSeverity: "warning" }),
        group({
          subjectId: ids(3),
          subjectName: "B-X 1002",
          worstSeverity: "critical",
          items: [item({ severity: "critical", snoozable: false })],
        }),
      ],
      { totals: { open: 2, critical: 1 } },
    ),
  );
  await page.goto("/");

  await expect(page.locator(".attention-card").first()).toHaveClass(/critical/);
});

test("warning group has one primary button and more actions", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await queue(page, attention([group()]));
  await page.goto("/");

  await expect(page.locator(".attention-card .ui-button.primary")).toHaveCount(1);
  await expect(page.getByRole("button", { name: "More actions" })).toBeVisible();
});

test("show more appears after eight groups", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await queue(
    page,
    attention(
      Array.from({ length: 9 }, (_, index) =>
        group({ subjectId: ids(index + 2), subjectName: `B-X 10${index}` }),
      ),
    ),
  );
  await page.goto("/");

  await expect(page.getByText("Show 1 more", { exact: true })).toBeVisible();
  await expect(page.locator(".attention-card")).toHaveCount(8);
});

test("primary button opens the work and calls no alert api", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await queue(page, attention([group()]));
  const alertRequests: string[] = [];
  page.on("request", (request) => {
    if (request.url().includes("/api/v1/alerts/")) alertRequests.push(request.url());
  });
  await page.goto("/");
  await page.getByRole("link", { name: "Verify" }).click();

  await expect(page).toHaveURL(/\/documents\?filter=queue$/);
  expect(alertRequests).toEqual([]);
});

test("upload link opens the prefilled dialog", async ({ page }) => {
  await session(page, ["hr"]);
  await queue(
    page,
    attention([
      group({
        subjectType: "employee",
        subjectId: ids(4),
        subjectName: "Alice Driver",
        items: [item({ ruleId: "A-02", scopeKey: "medical" })],
      }),
    ]),
  );
  await page.goto("/");
  await page.getByRole("link", { name: "Upload document" }).click();

  const uploadUrl = new URL(page.url());
  expect(uploadUrl.pathname).toBe("/documents");
  expect(uploadUrl.searchParams.get("upload")).toBe("employee:00000000-0000-0000-0000-000000000004:medical");
});

test("critical card offers no snooze", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await queue(
    page,
    attention(
      [group({ worstSeverity: "critical", items: [item({ severity: "critical", snoozable: false })] })],
      { totals: { open: 1, critical: 1 } },
    ),
  );
  await page.goto("/");

  await expect(page.getByRole("button", { name: "Snooze" })).toHaveCount(0);
});

test("snooze needs a reason and offers undo", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await queue(page, attention([group()]));
  const result: AlertActionResult = { id: ids(1), status: "snoozed", version: 2 };
  await page.route("**/api/v1/alerts/*/snooze", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(result) }),
  );
  await page.route("**/api/v1/alerts/*/unsnooze", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ...result, status: "open" }),
    }),
  );
  await page.goto("/");
  await page.getByRole("button", { name: "Snooze" }).click();
  await page.getByRole("button", { name: "Snooze", exact: true }).last().click();
  await expect(page.getByText("A reason is required.", { exact: true })).toBeVisible();
  await page.getByLabel("Reason", { exact: true }).fill("Awaiting carrier");
  await page.getByRole("button", { name: "Snooze", exact: true }).last().click();

  await expect(page.getByRole("button", { name: "Undo" })).toBeVisible();
});

test("owner sees view instead of actions", async ({ page }) => {
  await session(page, ["owner"]);
  await queue(page, attention([group()]));
  await page.goto("/");

  await expect(page.getByRole("link", { name: "View" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Snooze" })).toHaveCount(0);
});

test("owner kpi tile filters the queue", async ({ page }) => {
  await session(page, ["owner"]);
  await queue(
    page,
    attention(
      [
        group(),
        group({
          subjectId: ids(3),
          subjectName: "B-X 1002",
          worstSeverity: "critical",
          items: [item({ severity: "critical", snoozable: false })],
        }),
      ],
      { totals: { open: 2, critical: 1 } },
    ),
  );
  await page.goto("/");
  await page.getByRole("button", { name: /1 Critical/ }).click();

  await expect(page.locator(".attention-card")).toHaveCount(1);
  await expect(page.getByText("B-X 1002", { exact: true })).toBeVisible();
});

test("dispatcher sees no kpi strip", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await queue(page, attention([group()]));
  await page.goto("/");

  await expect(page.locator(".attention-kpis")).toHaveCount(0);
});

test("empty company shows guided setup", async ({ page }) => {
  await session(page, ["owner"]);
  await queue(page, attention([], { companyEmpty: true, totals: { open: 0, critical: 0 } }));
  await page.goto("/");

  await expect(page.getByText("Set up your company", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Add your first vehicle" })).toBeVisible();
});

test("quiet company shows nothing needs attention", async ({ page }) => {
  await session(page, ["dispatcher"]);
  await queue(page, attention([]));
  await page.goto("/");

  await expect(page.getByText("Nothing needs attention right now.", { exact: true })).toBeVisible();
});

test("attention has no accessibility violations", async ({ page }) => {
  await session(page, ["owner"]);
  await queue(page, attention([group()]));
  await page.goto("/");
  const results = await new AxeBuilder({ page: page as any }).analyze();

  expect(results.violations).toEqual([]);
});
