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
      id: "44444444-4444-4444-4444-444444444444",
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
});

test("row opens a quick view with overview, documents and a link to the full profile", async ({ page }) => {
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

test("documents cell opens the quick view on the documents tab", async ({ page }) => {
  await page.goto("/people");
  await page.getByRole("button", { name: /2 of 2 valid/ }).click();
  const dialog = page.getByRole("dialog", { name: "Dana Driver" });
  await expect(dialog.getByRole("tab", { name: /Documents/ })).toHaveAttribute("aria-selected", "true");
  const detail = personDetail();
  await expect(dialog.getByRole("listitem")).toHaveCount(detail.checklist.length);
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

test("blocked tile filters to people blocked from driving", async ({ page }) => {
  await page.goto("/people");
  const request = page.waitForRequest(
    (req) => req.url().includes("blockedOnly=true") && req.url().includes("pageSize=50"),
  );
  await page.getByRole("button", { name: /Blocked from driving/ }).click();
  await request;
  await expect(page.getByRole("button", { name: /Blocked from driving/ })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  await expect(page.getByRole("checkbox", { name: "Blocked only" })).toBeChecked();
});

test("office staff show not a driver and no phone", async ({ page }) => {
  await page.goto("/people");
  const olga = page.getByRole("row").filter({ hasText: "Olga Office" });
  await expect(olga.getByText("Not a driver", { exact: true })).toBeVisible();
  await expect(olga.getByText("Not recorded", { exact: true })).toBeVisible();
  await expect(olga.getByRole("button", { name: "Send enrolment" })).toHaveCount(0);
});

test("phone can be called from the row", async ({ page }) => {
  await page.goto("/people");
  await expect(page.getByRole("link", { name: "Call Dana Driver" })).toHaveAttribute(
    "href",
    "tel:+40123456789",
  );
  await expect(page.getByRole("button", { name: "Copy phone number of Dana Driver" })).toBeVisible();
});

test("upcoming absence shows in the row and the notice", async ({ page }) => {
  const list: components["schemas"]["AbsenceList"] = {
    items: [absence(plusDays(2), plusDays(4), "requested")],
  };
  await page.route("**/api/v1/absences?*", (route) => route.fulfill({ json: list }));
  await page.goto("/people");
  const dana = page.getByRole("row").filter({ hasText: "Dana Driver" });
  await expect(dana.getByText("Request waiting", { exact: true })).toBeVisible();
  await expect(page.getByRole("note")).toContainText("1 person is away");
  await expect(page.getByRole("note")).toContainText("1 request is waiting for a decision");
  await expect(page.getByRole("note").getByRole("link", { name: "Review requests" })).toHaveAttribute(
    "href",
    "/people/absences",
  );
  await dana.getByText("Request waiting", { exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Dana Driver" });
  await expect(dialog.getByRole("tab", { name: /Absences/ })).toHaveAttribute("aria-selected", "true");
  await expect(dialog.getByText("Requested", { exact: true })).toBeVisible();
});

test("people directory and quick view have no accessibility violations", async ({ page }) => {
  await page.goto("/people");
  await expect(page.getByRole("row").filter({ hasText: "Dana Driver" })).toBeVisible();
  const tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];
  const list = await new AxeBuilder({ page: page as never }).withTags(tags).analyze();
  expect(list.violations).toEqual([]);
  await page.getByRole("button", { name: "Quick view of Dana Driver" }).click();
  await expect(page.getByRole("dialog", { name: "Dana Driver" })).toBeVisible();
  const quick = await new AxeBuilder({ page: page as never }).withTags(tags).analyze();
  expect(quick.violations).toEqual([]);
});
