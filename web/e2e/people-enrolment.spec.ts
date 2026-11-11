import { peoplePage, personRow } from "./people-helpers";
import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import type { Session } from "../apps/office/src/auth/api";

const driverId = "11111111-1111-1111-1111-111111111111";
const endpoint = `**/api/v1/people/${driverId}/enrolment`;
const success: components["schemas"]["SendEnrolmentResult"] = {
  sentAt: "2026-11-09T10:00:00Z",
  testInbox: { code: "246810", personnelNumber: "D-100" },
};

test.beforeEach(async ({ page }) => {
  // The generated session endpoint currently declares no response body.
  const session: Session = { employeeId: "hr-user", roles: ["hr"], stepUpMethod: "password" };
  const drivers: components["schemas"]["PeoplePage"] = peoplePage([{ ...personRow, id: driverId }]);
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ json: session }));
  await page.route("**/api/v1/people?*", (route) => route.fulfill({ json: drivers }));
});

async function send(page: Page) {
  await page.goto("/people");
  await expect(page.getByText("Dana Driver", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Send enrolment", exact: true }).click();
}

test("hr sends enrolment and sees the test inbox", async ({ page }) => {
  await page.route(endpoint, (route) => route.fulfill({ json: success }));
  const request = page.waitForRequest(endpoint);
  await send(page);
  expect((await request).method()).toBe("POST");
  await expect(page.getByText("Enrolment sent to Dana Driver.", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Test inbox" })).toHaveText(
    "Test inbox: code 246810, personnel number D-100",
  );
});

test("no test inbox block without test data", async ({ page }) => {
  const result: components["schemas"]["SendEnrolmentResult"] = { ...success, testInbox: null };
  await page.route(endpoint, (route) => route.fulfill({ json: result }));
  await send(page);
  await expect(page.getByText("Enrolment sent to Dana Driver.", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Test inbox" })).toHaveCount(0);
  await expect(page.getByText(/Test inbox:/)).toHaveCount(0);
});

test("refusal explains what is missing", async ({ page }) => {
  const problem: components["schemas"]["ProblemDetails"] = { status: 409 };
  await page.route(endpoint, (route) => route.fulfill({ status: 409, json: problem }));
  await send(page);
  await expect(page.getByRole("alert")).toHaveText("This driver needs a personnel number and phone first.");
  await expect(page.getByRole("region", { name: "Test inbox" })).toHaveCount(0);
});

test("send enrolment uses the office button style", async ({ page }) => {
  await page.goto("/people");
  await expect(page.getByRole("button", { name: "Send enrolment", exact: true })).toHaveClass(
    /\bui-button\b/,
  );
});

test("forbidden is not shown as missing data", async ({ page }) => {
  const problem: components["schemas"]["ProblemDetails"] = { status: 403 };
  await page.route(endpoint, (route) => route.fulfill({ status: 403, json: problem }));
  await send(page);
  await expect(page.getByRole("alert")).toHaveText("Enrolment could not be sent. Please try again.");
  await expect(
    page.getByText("This driver needs a personnel number and phone first.", { exact: true }),
  ).toHaveCount(0);
  await expect(page.getByRole("region", { name: "Test inbox" })).toHaveCount(0);
});
