import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";

// Epic 12 browser tests (Stories 12.4–12.7), written by the orchestrator (user's choice, 2026-11-12).
type Schema = components["schemas"];
const colleagueId = "00000000-0000-0000-0000-000000000051";
const coverId = "00000000-0000-0000-0000-000000000052";
const personId = "00000000-0000-0000-0000-000000000053";

async function meRoutes(page: Page, covers: Schema["AlertCoverItem"][] = []) {
  let prefs: Schema["NotifyPreferences"] = { criticalPush: true, criticalEmail: true, digestEmail: true };
  const puts: Schema["NotifyPreferences"][] = [];
  await page.route("**/api/v1/me/notify-prefs", async (route) => {
    if (route.request().method() === "PUT") {
      prefs = route.request().postDataJSON();
      puts.push(prefs);
    }
    await route.fulfill({ json: prefs });
  });
  await page.route("**/api/v1/me/push", (route) =>
    route.fulfill({ json: { enabled: false, publicKey: null } satisfies Schema["PushStatus"] }),
  );
  await page.route("**/api/v1/me/alert-covers", (route) =>
    route.fulfill({ json: { items: covers } satisfies Schema["AlertCoverList"] }),
  );
  const people: Schema["PeoplePage"] = {
    items: [
      {
        id: colleagueId,
        name: "Hanna Hartmann",
        roles: ["dispatcher"],
        status: "active",
        canDrive: false,
        reasonCategory: null,
        validDocuments: 0,
        requiredDocuments: 0,
        worstState: null,
        defaultVehiclePlate: null,
        phone: null,
      },
    ],
    page: 1,
    pageSize: 200,
    total: 1,
  };
  await page.route("**/api/v1/people?*", (route) => route.fulfill({ json: people }));
  return puts;
}

test("my notifications saves preferences", async ({ page }) => {
  const puts = await meRoutes(page);
  await page.goto("/me/notifications");
  await expect(page.getByRole("heading", { level: 1, name: "My notifications" })).toBeVisible();
  const email = page.getByRole("switch", { name: "E-mail for critical alerts" });
  await expect(email).toHaveAttribute("aria-checked", "true");
  await email.click();
  await expect
    .poll(() => puts.at(-1))
    .toEqual({ criticalPush: true, criticalEmail: false, digestEmail: true });
  await expect(email).toHaveAttribute("aria-checked", "false");
  await expect(page.getByText("Push is not set up on this server yet.", { exact: false })).toBeVisible();
});

test("cover shows the colleague and dates", async ({ page }) => {
  await meRoutes(page, [
    { id: coverId, toId: colleagueId, colleague: "Hanna Hartmann", from: "2026-11-16", to: "2026-11-20" },
  ]);
  let posted: Schema["AlertCoverRequest"] | undefined;
  await page.route("**/api/v1/me/alert-covers", async (route) => {
    if (route.request().method() === "POST") {
      posted = route.request().postDataJSON();
      await route.fulfill({ json: { items: [] } satisfies Schema["AlertCoverList"] });
      return;
    }
    await route.fallback();
  });
  await page.goto("/me/notifications");
  await expect(page.getByText("Hanna Hartmann · 16.11.2026 – 20.11.2026", { exact: false })).toBeVisible();
  await page.getByLabel("Colleague", { exact: true }).selectOption(colleagueId);
  await page.getByLabel("From", { exact: true }).fill("2026-12-01");
  await page.getByLabel("To", { exact: true }).fill("2026-11-30");
  await page.getByRole("button", { name: "Add cover", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("The end date must be on or after the start date.");
  expect(posted).toBeUndefined();
  await page.getByLabel("To", { exact: true }).fill("2026-12-05");
  await page.getByRole("button", { name: "Add cover", exact: true }).click();
  await expect.poll(() => posted).toEqual({ toId: colleagueId, from: "2026-12-01", to: "2026-12-05" });
});

test("owner kpi strip links to details", async ({ page }) => {
  const overview: Schema["OwnerOverview"] = {
    items: [
      { key: "criticalAlerts", value: 2, status: null, href: "/?severity=critical" },
      { key: "trucksOffRoad", value: 1, status: null, href: "/fleet?status=off_road" },
      { key: "documentsCompletePercent", value: 87.5, status: null, href: "/documents" },
      { key: "exportStatus", value: null, status: "sent", href: "/payroll" },
      { key: "maintenanceCostEur", value: 1250, status: null, href: "/fleet/maintenance" },
    ],
  };
  await page.route("**/api/v1/owner/overview", (route) => route.fulfill({ json: overview }));
  await page.goto("/");
  const strip = page.getByRole("region", { name: "Company overview" });
  await expect(strip.getByRole("button")).toHaveCount(5);
  await expect(strip).toContainText("Trucks off road");
  await expect(strip).toContainText("88 %");
  await expect(strip).toContainText("Sent");
  await expect(strip).not.toContainText(/hours/i);
  await strip.getByRole("button", { name: /Trucks off road/ }).click();
  await expect(page).toHaveURL(/\/fleet\?status=off_road$/);
});

test("audit log filters and exports", async ({ page }) => {
  const queries: string[] = [];
  const pageOf = (rows: Schema["AuditRow"][]): Schema["AuditPage"] => ({
    items: rows,
    total: rows.length,
    page: 1,
    pageSize: 50,
  });
  const row: Schema["AuditRow"] = {
    id: "00000000-0000-0000-0000-000000000061",
    time: "2026-11-04T09:00:00Z",
    who: "Olivia Owner",
    entityType: "Employee",
    entityId: personId,
    field: "last_name",
    oldValue: "Before",
    newValue: "After",
    reason: "Typo",
  };
  await page.route("**/api/v1/audit-log?*", (route) => {
    queries.push(new URL(route.request().url()).search);
    return route.fulfill({ json: pageOf([row]) });
  });
  let exported = "";
  await page.route("**/api/v1/audit-log/export?*", (route) => {
    exported = new URL(route.request().url()).search;
    return route.fulfill({ status: 200, contentType: "text/csv", body: "time,who\n" });
  });
  await page.goto("/audit");
  await expect(page.getByRole("heading", { level: 1, name: "Audit log" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Olivia Owner", exact: true })).toBeVisible();
  await page.getByLabel("Entity", { exact: true }).selectOption("Vehicle");
  await page.getByLabel("From", { exact: true }).fill("2026-11-01");
  await expect.poll(() => queries.at(-1)).toContain("EntityType=Vehicle");
  expect(queries.at(-1)).toContain("From=2026-11-01");
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export", exact: true }).click();
  expect((await download).suggestedFilename()).toBe("audit-log.csv");
  expect(exported).toContain("EntityType=Vehicle");
});

test("special field shows changed only", async ({ page }) => {
  const masked: Schema["AuditPage"] = {
    items: [
      {
        id: "00000000-0000-0000-0000-000000000062",
        time: "2026-11-03T09:00:00Z",
        who: "Hanna Hartmann",
        entityType: "Employee",
        entityId: personId,
        field: "phone",
        oldValue: "changed",
        newValue: "changed",
        reason: "changed",
      },
    ],
    total: 1,
    page: 1,
    pageSize: 50,
  };
  await page.route("**/api/v1/audit-log?*", (route) => route.fulfill({ json: masked }));
  await page.goto("/audit");
  const row = page.getByRole("row").filter({ hasText: "phone" });
  await expect(row.getByRole("cell")).toHaveText([
    /.+/,
    "Hanna Hartmann",
    "Employee",
    "phone",
    "changed",
    "changed",
    "changed",
  ]);
  await expect(page.getByText(/\+49/)).toHaveCount(0);
});
