import { test, expect } from "./fixtures";
import AxeBuilder from "@axe-core/playwright";
import type { components } from "@fleetops/api-client";
import { personId, personRow, personDetail, officeSession, peoplePage, mockPerson } from "./people-helpers";

const office: components["schemas"]["PersonRow"] = {
  ...personRow,
  id: "33333333-3333-3333-3333-333333333333",
  name: "Olga Office",
  roles: ["dispatcher"],
  validDocuments: 0,
  requiredDocuments: 0,
  worstState: null,
  defaultVehiclePlate: null,
  phone: null,
};
const absenceId = "44444444-4444-4444-4444-444444444444";

function berlinToday() {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Berlin" }).format(new Date());
}

function plusDays(days: number) {
  const value = new Date(`${berlinToday()}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return value.toISOString().slice(0, 10);
}

function absence(
  from: string,
  to: string,
  status: components["schemas"]["AbsenceStatus"],
): components["schemas"]["AbsenceDetail"] {
  return {
    absence: {
      id: absenceId,
      employeeId: personId,
      from,
      to,
      halfDay: null,
      type: "vacation",
      status,
      note: null,
      declineReason: null,
      cancelReason: null,
    },
    employeeName: personRow.name,
    workingDays: 3,
    balancesBefore: [],
    balancesAfter: [],
    othersAbsentCount: 0,
    othersAbsentNames: [],
    dayCounting: "Mon\u2013Fri",
  };
}

test.beforeEach(async ({ page }) => {
  await officeSession(page, ["hr"]);
  await page.route("**/api/v1/people?*", (route) => {
    const url = new URL(route.request().url());
    const role = url.searchParams.get("role");
    const items = [personRow, office].filter((person) => !role || person.roles.includes(role));
    return route.fulfill({ json: peoplePage(items) });
  });
  await mockPerson(page);
  const none: components["schemas"]["AbsenceList"] = { items: [] };
  await page.route("**/api/v1/absences?*", (route) => route.fulfill({ json: none }));
  const balance: components["schemas"]["VacationBalance"] = {
    year: 2026,
    opening: 2,
    entitlement: 28,
    used: 14,
    remaining: 16,
  };
  await page.route("**/api/v1/people/*/vacation-balance?*", (route) => route.fulfill({ json: balance }));
});

test("row opens a drawer with overview, documents and a link to the full profile", async ({ page }) => {
  await page.goto("/people");
  await page.getByRole("row").filter({ hasText: "Dana Driver" }).getByRole("cell").nth(1).click();
  const dialog = page.getByRole("dialog", { name: "Dana Driver" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("tab", { name: "Overview" })).toHaveAttribute("aria-selected", "true");
  await expect(dialog.getByText("D-100", { exact: true })).toBeVisible();
  await expect(dialog.getByText("B-X 1001", { exact: true })).toBeVisible();
  await dialog.getByRole("tab", { name: "Documents" }).click();
  await expect(dialog.getByText("2 of 2 required documents verified.", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("link", { name: "Open full profile" })).toHaveAttribute(
    "href",
    `/people/${personId}`,
  );
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
});

test("documents cell opens the drawer on the documents tab", async ({ page }) => {
  await page.goto("/people");
  await page.getByRole("button", { name: /2 of 2 valid/ }).click();
  const dialog = page.getByRole("dialog", { name: "Dana Driver" });
  await expect(dialog.getByRole("tab", { name: /Documents/ })).toHaveAttribute("aria-selected", "true");
  await expect(dialog.getByRole("listitem")).toHaveCount(personDetail().checklist.length);
});

test("role tab filters the list and shows counts", async ({ page }) => {
  await page.goto("/people");
  const roles = page.getByRole("group", { name: "Filter by role" });
  await expect(roles.getByRole("button", { name: "Everyone 2" })).toHaveAttribute("aria-pressed", "true");
  await expect(roles.getByRole("button", { name: "Drivers 1" })).toBeVisible();
  const request = page.waitForRequest((req) => req.url().includes("role=driver"));
  await roles.getByRole("button", { name: "Drivers 1" }).click();
  await request;
  await expect(page.getByRole("row").filter({ hasText: "Olga Office" })).toHaveCount(0);
  await expect(page.getByRole("row").filter({ hasText: "Dana Driver" })).toHaveCount(1);
});

test("blocked chip filters to people blocked from driving", async ({ page }) => {
  await page.goto("/people");
  const request = page.waitForRequest(
    (req) => req.url().includes("blockedOnly=true") && req.url().includes("pageSize=50"),
  );
  await page.getByRole("checkbox", { name: "Blocked only" }).check();
  await request;
  await expect(page.getByRole("button", { name: "Clear filters" })).toBeVisible();
});

test("office staff show not a driver, no phone and no truck", async ({ page }) => {
  await page.goto("/people");
  const olga = page.getByRole("row").filter({ hasText: "Olga Office" });
  await expect(olga.getByText("Not a driver", { exact: true })).toBeVisible();
  await expect(olga.getByText("Not recorded", { exact: true })).toBeVisible();
  await expect(olga.getByText("\u2014 Not relevant", { exact: true })).toBeVisible();
  await expect(olga.getByRole("button", { name: "Send enrolment" })).toHaveCount(0);
  await expect(olga.getByRole("link", { name: "Timesheet of Olga Office" })).toHaveCount(0);
});

test("phone can be called and the timesheet opened from the row", async ({ page }) => {
  await page.goto("/people");
  await expect(page.getByRole("link", { name: "Call Dana Driver" })).toHaveAttribute(
    "href",
    "tel:+40123456789",
  );
  await expect(page.getByRole("button", { name: "Copy phone number of Dana Driver" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Timesheet of Dana Driver" })).toHaveAttribute(
    "href",
    "/timesheets?search=Dana%20Driver",
  );
});

test("view switcher links People, Timesheets and Absences", async ({ page }) => {
  await page.goto("/people");
  const views = page.getByRole("navigation", { name: "People views" });
  await expect(views.getByRole("link", { name: /People/ })).toHaveAttribute("aria-current", "page");
  await expect(views.getByRole("link", { name: /Timesheets/ })).toHaveAttribute("href", "/timesheets");
  await expect(views.getByRole("link", { name: "Absences & capacity" })).toHaveAttribute(
    "href",
    "/people/absences",
  );
});

test("leave request shows in the row, the pill and the drawer, and can be approved", async ({ page }) => {
  const list: components["schemas"]["AbsenceList"] = {
    items: [absence(plusDays(2), plusDays(4), "requested")],
  };
  await page.route("**/api/v1/absences?*", (route) => route.fulfill({ json: list }));
  await page.goto("/people");
  const dana = page.getByRole("row").filter({ hasText: "Dana Driver" });
  await expect(dana.getByText("Request waiting", { exact: true })).toBeVisible();
  const pill = page.getByRole("link", { name: /1 leave request is waiting for a decision/ });
  await expect(pill).toHaveAttribute("href", "/people/absences");
  await dana.getByText("Request waiting", { exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Dana Driver" });
  await expect(dialog.getByRole("tab", { name: /Leave/ })).toHaveAttribute("aria-selected", "true");
  await expect(dialog.getByText("16 days", { exact: true })).toBeVisible();
  let body: unknown;
  await page.route(`**/api/v1/absences/${absenceId}/decide`, async (route) => {
    body = route.request().postDataJSON();
    await route.fulfill({ json: absence(plusDays(2), plusDays(4), "approved") });
  });
  await dialog.getByRole("button", { name: "Approve request" }).click();
  await expect.poll(() => body).toEqual({ approve: true, reason: null, confirmOverlap: false });
});

test("capacity strip shows the next five weeks", async ({ page }) => {
  const list: components["schemas"]["AbsenceList"] = {
    items: [absence(plusDays(7), plusDays(9), "approved")],
  };
  await page.route("**/api/v1/absences?*", (route) => route.fulfill({ json: list }));
  await page.goto("/people");
  const strip = page.getByRole("region", { name: "Driver capacity, next 5 weeks" });
  await expect(strip.getByRole("listitem")).toHaveCount(5);
  await expect(strip.getByRole("listitem").first()).toContainText("(now)");
  await expect(strip).toContainText("% away");
});

test("data FleetOps does not store yet is shown as Epic 15 placeholders", async ({ page }) => {
  await page.goto("/people");
  await page.getByRole("button", { name: "Quick view of Dana Driver" }).click();
  const dialog = page.getByRole("dialog", { name: "Dana Driver" });
  await dialog.getByRole("tab", { name: /Cards/ }).click();
  for (const title of ["Fuel card", "Toll devices", "Per diem this week"])
    await expect(dialog.getByText(title, { exact: true })).toBeVisible();
  await expect(dialog.getByRole("tabpanel").getByText("Coming in Epic 15", { exact: true })).toHaveCount(3);
  await expect(dialog.getByRole("button", { name: "Message to driver app" })).toBeDisabled();
});

test("people page and drawer have no accessibility violations", async ({ page }) => {
  await page.goto("/people");
  await expect(page.getByRole("row").filter({ hasText: "Dana Driver" })).toBeVisible();
  const tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];
  const list = await new AxeBuilder({ page: page as never }).withTags(tags).analyze();
  expect(list.violations).toEqual([]);
  await page.getByRole("button", { name: "Quick view of Dana Driver" }).click();
  await expect(page.getByRole("dialog", { name: "Dana Driver" })).toBeVisible();
  const drawer = await new AxeBuilder({ page: page as never }).withTags(tags).analyze();
  expect(drawer.violations).toEqual([]);
});

test("drawer says when no vacation entitlement is recorded", async ({ page }) => {
  const empty: components["schemas"]["VacationBalance"] = {
    year: 2026,
    opening: 0,
    entitlement: 0,
    used: 0,
    remaining: 0,
  };
  await page.route("**/api/v1/people/*/vacation-balance?*", (route) => route.fulfill({ json: empty }));
  await page.goto("/people");
  await page.getByRole("button", { name: "Quick view of Dana Driver" }).click();
  const dialog = page.getByRole("dialog", { name: "Dana Driver" });
  await dialog.getByRole("tab", { name: /Leave/ }).click();
  await expect(
    dialog.getByText("No vacation entitlement recorded. Set it with Edit fields on the full profile.", {
      exact: true,
    }),
  ).toBeVisible();
  await expect(dialog.getByText("0 days", { exact: true })).toHaveCount(0);
});
