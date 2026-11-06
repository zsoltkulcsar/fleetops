import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const session = (roles: string[] = ["owner"]) => ({
  status: 200,
  contentType: "application/json",
  body: JSON.stringify({ employeeId: "e-1", roles }),
});
async function noSession(page: Page) {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 401,
      contentType: "application/problem+json",
      body: JSON.stringify({ code: "no_session" }),
    }),
  );
}
async function signIn(page: Page, next = "done") {
  await page.route("**/api/v1/auth/sign-in", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ next }) }),
  );
}

test("no session shows sign in at the requested route", async ({ page }) => {
  await noSession(page);
  await page.goto("/timesheets");
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await expect(page).toHaveURL(/\/timesheets$/);
});
test("signing in returns to the requested route", async ({ page }) => {
  let signedIn = false;
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill(
      signedIn
        ? session()
        : {
            status: 401,
            contentType: "application/problem+json",
            body: JSON.stringify({ code: "no_session" }),
          },
    ),
  );
  await page.route("**/api/v1/auth/sign-in", async (route) => {
    signedIn = true;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ next: "done" }),
    });
  });
  await page.goto("/fleet");
  await page.getByLabel("E-mail address").fill("demo@example.com");
  await page.getByLabel("Password").fill("correct password");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page).toHaveURL(/\/fleet$/);
});
test("wrong details show one message for wrong email and wrong password", async ({ page }) => {
  await noSession(page);
  await page.route("**/api/v1/auth/sign-in", (route) =>
    route.fulfill({
      status: 401,
      contentType: "application/json",
      body: JSON.stringify({ code: "sign_in_failed" }),
    }),
  );
  await page.goto("/");
  for (const email of ["wrong@example.com", "right@example.com"]) {
    await page.getByLabel("E-mail address").fill(email);
    await page.getByLabel("Password").fill("wrong");
    await page.getByRole("button", { name: "Continue" }).click();
    await expect(page.getByRole("alert")).toHaveText("The e-mail address or password is incorrect.");
    await expect(page.getByRole("alert")).toHaveCount(1);
  }
});
test("locked account shows the locked message", async ({ page }) => {
  await noSession(page);
  await page.route("**/api/v1/auth/sign-in", (route) =>
    route.fulfill({ status: 423, body: JSON.stringify({ code: "account_locked" }) }),
  );
  await page.goto("/");
  await page.getByLabel("E-mail address").fill("locked@example.com");
  await page.getByLabel("Password").fill("password");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("alert")).toContainText("temporarily locked");
});
test("second factor step is required when next says so", async ({ page }) => {
  await noSession(page);
  await signIn(page, "second_factor");
  await page.goto("/");
  await page.getByLabel("E-mail address").fill("hr@example.com");
  await page.getByLabel("Password").fill("password");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("heading", { name: "Enter your verification code" })).toBeVisible();
});
test("wrong second factor code shows a persistent error", async ({ page }) => {
  await noSession(page);
  await signIn(page, "second_factor");
  await page.route("**/api/v1/auth/second-factor", (route) =>
    route.fulfill({ status: 401, body: JSON.stringify({ code: "second_factor_failed" }) }),
  );
  await page.goto("/");
  await page.getByLabel("E-mail address").fill("hr@example.com");
  await page.getByLabel("Password").fill("password");
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByLabel("Code").fill("000000");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("alert")).toContainText("verification code is incorrect");
});
test("a wrong second factor code does not show the expired notice", async ({ page }) => {
  await noSession(page);
  await signIn(page, "second_factor");
  await page.route("**/api/v1/auth/second-factor", (route) =>
    route.fulfill({ status: 401, body: JSON.stringify({ code: "second_factor_failed" }) }),
  );
  await page.goto("/");
  await page.getByLabel("E-mail address").fill("hr@example.com");
  await page.getByLabel("Password").fill("password");
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByLabel("Code").fill("000000");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByText("Your session has expired. Please sign in again.")).toHaveCount(0);
});
test("enrolment shows the secret and confirms with a code", async ({ page }) => {
  await noSession(page);
  await signIn(page, "enrol_second_factor");
  await page.route("**/api/v1/auth/second-factor/enrol", (route) =>
    route.fulfill({
      status: 200,
      body: JSON.stringify({ secret: "ABC123", otpauthUri: "otpauth://totp/FleetOps" }),
    }),
  );
  await page.route("**/api/v1/auth/second-factor", (route) =>
    route.fulfill({ status: 200, body: JSON.stringify({ next: "done" }) }),
  );
  await page.goto("/");
  await page.getByLabel("E-mail address").fill("owner@example.com");
  await page.getByLabel("Password").fill("password");
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByText("ABC123")).toBeVisible();
  await page.getByLabel("Code").fill("123456");
  await page.getByRole("button", { name: "Continue" }).click();
});
test("privacy notice must be acknowledged before the app opens", async ({ page }) => {
  await noSession(page);
  await signIn(page, "privacy_notice");
  await page.route("**/api/v1/auth/privacy-notice", (route) =>
    route.fulfill({ status: 200, body: JSON.stringify({ next: "done" }) }),
  );
  await page.goto("/");
  await page.getByLabel("E-mail address").fill("owner@example.com");
  await page.getByLabel("Password").fill("password");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByText("placeholder and has not yet been confirmed")).toBeVisible();
  await expect(page.getByRole("button", { name: "Continue" })).toBeDisabled();
  await page.getByRole("checkbox").check();
  await expect(page.getByRole("button", { name: "Continue" })).toBeEnabled();
});
test("forgot password shows the same confirmation for any email", async ({ page }) => {
  await noSession(page);
  await page.route("**/api/v1/auth/forgot-password", (route) => route.fulfill({ status: 202 }));
  await page.goto("/?forgot=1");
  const confirmation = page.getByText(/If an account matches/);
  await page.getByLabel("E-mail address").fill("any@example.com");
  await page.getByRole("button", { name: "Continue" }).click();
  const firstConfirmation = await confirmation.innerText();
  expect(firstConfirmation.trim()).not.toBe("");
  expect(firstConfirmation).toContain("If an account matches");

  await page.getByRole("link", { name: "Back to sign in" }).click();
  await page.goto("/?forgot=1");
  await page.getByLabel("E-mail address").fill("different@example.com");
  await page.getByRole("button", { name: "Continue" }).click();
  const secondConfirmation = await confirmation.innerText();
  expect(secondConfirmation.trim()).not.toBe("");
  expect(secondConfirmation).toContain("If an account matches");
  expect(secondConfirmation).toBe(firstConfirmation);
});
test("a failed request on an auth form shows an error and not silence", async ({ page }) => {
  await noSession(page);
  await page.route("**/api/v1/auth/forgot-password", (route) => route.abort());
  await page.goto("/?forgot=1");
  await page.getByLabel("E-mail address").fill("any@example.com");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("alert")).toContainText(
    "Something went wrong. Check your connection and try again.",
  );
});
test("reset with a bad token shows an error and a way back", async ({ page }) => {
  await noSession(page);
  await page.route("**/api/v1/auth/reset-password", (route) =>
    route.fulfill({ status: 400, body: JSON.stringify({ code: "reset_failed" }) }),
  );
  await page.goto("/?reset=1&token=bad");
  await page.getByLabel("Password").fill("a very long invalid password");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("alert")).toContainText("invalid or has expired");
  await expect(page.getByRole("link", { name: "Back to sign in" })).toBeVisible();
});
test("a 401 on an auth request does not show the expired notice", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) => route.fulfill(session()));
  await page.goto("/");
  await page.route("**/api/v1/auth/sign-out", (route) =>
    route.fulfill({ status: 401, body: JSON.stringify({ code: "session_expired" }) }),
  );
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("alert")).toHaveCount(0);
});
test("a 401 on a later request returns to sign in with the expired notice", async ({ page }) => {
  let signedIn = false;
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill(
      signedIn
        ? session()
        : {
            status: 401,
            contentType: "application/problem+json",
            body: JSON.stringify({ code: "no_session" }),
          },
    ),
  );
  await page.route("**/api/v1/auth/sign-in", async (route) => {
    signedIn = true;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ next: "done" }),
    });
  });
  await page.goto("/");
  await page.getByLabel("E-mail address").fill("demo@example.com");
  await page.getByLabel("Password").fill("correct password");
  await page.getByRole("button", { name: "Continue" }).click();
  await page.route("**/api/v1/fleet/vehicles", (route) =>
    route.fulfill({
      status: 401,
      contentType: "application/problem+json",
      body: JSON.stringify({ code: "session_expired" }),
    }),
  );
  await page.evaluate(() => fetch("/api/v1/fleet/vehicles"));
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await expect(
    page.getByText("Your session has expired. Please sign in again.", { exact: true }),
  ).toBeVisible();
});
test("sign out returns to sign in", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) => route.fulfill(session()));
  await page.goto("/");
  await page.route("**/api/v1/auth/sign-out", (route) => route.fulfill({ status: 204 }));
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
});
test("unsafe auth requests send the custom header", async ({ page }) => {
  await noSession(page);
  let header = "";
  await page.route("**/api/v1/auth/sign-in", (route) => {
    header = route.request().headers()["x-fleetops-request"];
    return route.fulfill({ status: 401, body: JSON.stringify({ code: "sign_in_failed" }) });
  });
  await page.goto("/");
  await page.getByLabel("E-mail address").fill("a@example.com");
  await page.getByLabel("Password").fill("password");
  await page.getByRole("button", { name: "Continue" }).click();
  expect(header).toBe("1");
});
test("dispatcher does not see Payroll export or Settings", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) => route.fulfill(session(["dispatcher"])));
  await page.goto("/");
  await expect(page.getByRole("link", { name: "Payroll export" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Settings" })).toHaveCount(0);
});
test("owner sees every nav item", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) => route.fulfill(session()));
  await page.goto("/");
  if ((await page.evaluate(() => window.innerWidth)) < 768) {
    await page.getByRole("button", { name: "More" }).click();
  }
  for (const item of [
    "Attention",
    "Timesheets",
    "Fleet",
    "People",
    "Documents",
    "Payroll export",
    "Settings",
  ])
    await expect(page.getByRole("link", { name: item, exact: true })).toBeVisible();
});
test("sign in screens have no WCAG violations in both themes", async ({ page }) => {
  await noSession(page);
  for (const colorScheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme });
    await page.goto("/");
    const result = await new AxeBuilder({ page: page as never }).analyze();
    expect(result.violations, JSON.stringify(result.violations)).toEqual([]);
  }
});
test("loading never flashes the shell", async ({ page }) => {
  await page.route("**/api/v1/auth/session", async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 1000));
    await route.fulfill({ status: 401, body: JSON.stringify({ code: "no_session" }) });
  });
  await page.goto("/");
  await expect(page.locator(".sidebar")).toHaveCount(0);
  await expect(page.locator(".auth-loading")).toBeVisible();
});
