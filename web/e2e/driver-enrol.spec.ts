import { test, expect, type Request } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import {
  driverRoutes,
  openEnrol,
  submitEnrol,
  reachPin,
  setPin,
  enterPin,
  storedVault,
  pin,
} from "./driver-helpers";

test.beforeEach(async ({ page }) => driverRoutes(page));

test("install guide shows iphone steps", async ({ browser, baseURL }) => {
  const context = await browser.newContext({
    baseURL,
    userAgent:
      "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1",
  });
  try {
    const page = await context.newPage();
    await page.goto("/driver/");
    await expect(page.getByRole("listitem")).toHaveText(["Tap Share", "Then Add to Home Screen"]);
  } finally {
    await context.close();
  }
});

test("install guide can be skipped and stays skipped", async ({ page }) => {
  await openEnrol(page);
  await page.reload();
  await expect(page.getByRole("heading", { name: "Set up your driver app" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Install FleetOps Driver" })).toHaveCount(0);
});

test("enrol moves to privacy then pin", async ({ page }) => {
  await openEnrol(page);
  const enrolRequest = page.waitForRequest("**/api/v1/auth/driver/enrol");
  await submitEnrol(page);
  const expected: components["schemas"]["EnrolDriverCommand"] = { personnelNumber: "D-100", code: "123456" };
  expect((await enrolRequest).postDataJSON()).toEqual(expected);
  await expect(page.getByRole("heading", { name: "Privacy notice", exact: true })).toBeVisible();
  await expect(page.getByLabel("Confirm PIN", { exact: true })).toHaveCount(0);
  const privacyRequest = page.waitForRequest("**/api/v1/auth/privacy-notice");
  await page.getByRole("button", { name: "I acknowledge the privacy notice" }).click();
  const privacy: components["schemas"]["PrivacyRequest"] = { version: "1" };
  expect((await privacyRequest).postDataJSON()).toEqual(privacy);
  await expect(page.getByLabel("Confirm PIN", { exact: true })).toBeVisible();
});

test("wrong code shows one message", async ({ page }) => {
  const problem: components["schemas"]["ProblemDetails"] = { status: 400 };
  await page.route("**/api/v1/auth/driver/enrol", (route) => route.fulfill({ status: 400, json: problem }));
  await openEnrol(page);
  await submitEnrol(page);
  await expect(page.getByRole("alert")).toHaveCount(1);
  await expect(page.getByRole("alert")).toHaveText(
    "That code or number is not right. Ask the office for a new code if this keeps happening.",
  );
  await expect(page.getByRole("heading", { name: "Set up your driver app" })).toBeVisible();
});

test("pin mismatch is refused", async ({ page }) => {
  await reachPin(page);
  await page.getByLabel("PIN", { exact: true }).fill(pin);
  await page.getByLabel("Confirm PIN", { exact: true }).fill("123789");
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("The PINs do not match. Try again.");
  await expect(page.getByRole("heading", { name: "Set your PIN" })).toBeVisible();
  expect(await storedVault(page)).toBeUndefined();
});

test("pin never reaches the server", async ({ page, context }) => {
  await reachPin(page);
  const captures: Promise<{ url: string; headers: Record<string, string>; body: string | null }>[] = [];
  const capture = (request: Request) => {
    captures.push(
      request.allHeaders().then((headers) => ({ url: request.url(), headers, body: request.postData() })),
    );
  };
  context.on("request", capture);
  await setPin(page);
  await page.reload();
  await enterPin(page);
  await expect(page.getByRole("navigation", { name: "Driver navigation" })).toBeVisible();
  await expect.poll(() => captures.length).toBeGreaterThan(0);
  context.off("request", capture);
  const requests = await Promise.all(captures);
  expect(requests.filter((request) => request.url.endsWith("/api/v1/auth/session")).length).toBeGreaterThan(
    0,
  );
  for (const request of requests) {
    expect(decodeURIComponent(JSON.stringify(request))).not.toContain(pin);
  }
});
