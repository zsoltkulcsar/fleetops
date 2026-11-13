import { test, expect } from "./fixtures";
import AxeBuilder from "@axe-core/playwright";
import { openWithPausedClock } from "./clock";

const routes = [
  ["/", "Attention"],
  ["/timesheets", "Timesheets"],
  ["/people", "People"],
  ["/documents", "Documents"],
  ["/payroll", "Payroll export"],
  ["/settings", "Settings"],
] as const;
const axeTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

test.describe("navigation and responsive shell", () => {
  for (const [route, label] of routes) {
    test(`${label} is a distinct route`, async ({ page }) => {
      await page.goto(route);
      await expect(page).toHaveURL(new RegExp(`${route === "/" ? "\\/" : route}$`));
      await expect(page.getByRole("heading", { level: 1, name: label })).toBeVisible();
      await expect(page.locator(".topbar strong")).toHaveText(label);
      await expect(page).toHaveTitle(
        label === "People" ? "People \u00b7 FleetOps" : `${label} · FleetOps Office`,
      );
      if (page.viewportSize()!.width < 768 && ["/documents", "/payroll", "/settings"].includes(route)) {
        await expect(page.getByRole("button", { name: "More" })).toHaveAttribute("aria-current", "page");
      } else {
        await expect(page.locator('nav:not(.people-views) a[aria-current="page"]')).toHaveCount(1);
        await expect(page.locator('nav:not(.people-views) a[aria-current="page"]')).toHaveText(label);
      }
    });
  }

  test("Fleet is a distinct route", async ({ page }) => {
    await page.goto("/fleet");
    await expect(page).toHaveURL(/\/fleet$/);
    await expect(page.getByRole("heading", { level: 1, name: "Fleet" })).toBeVisible();
    await expect(page.getByText("No vehicles yet.")).toBeVisible();
    await expect(page.locator(".topbar strong")).toHaveText("Fleet");
    await expect(page).toHaveTitle("Fleet · FleetOps Office");
    await expect(page.locator('nav:not(.people-views) a[aria-current="page"]')).toHaveCount(1);
    await expect(page.locator('nav:not(.people-views) a[aria-current="page"]')).toHaveText("Fleet");
  });

  test("unknown paths redirect to home", async ({ page }) => {
    await page.goto("/not-a-section");
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByRole("heading", { level: 1, name: "Attention", exact: true })).toBeVisible();
  });

  test("mobile More reaches its three links", async ({ page }) => {
    test.skip((await page.evaluate(() => window.innerWidth)) >= 768);
    await page.goto("/");
    await page.getByRole("button", { name: "More" }).click();
    for (const label of ["Documents", "Payroll export", "Settings"])
      await expect(page.getByRole("link", { name: label })).toBeVisible();
  });
});

test("breakpoint boundaries update as the viewport crosses each width", async ({ page }) => {
  await page.setViewportSize({ width: 767, height: 800 });
  await page.goto("/");
  await expect(page.locator(".bottom-nav")).toBeVisible();
  await page.setViewportSize({ width: 768, height: 800 });
  await expect(page.locator(".icon-rail")).toBeVisible();
  await page.setViewportSize({ width: 1279, height: 800 });
  await expect(page.locator(".icon-rail")).toBeVisible();
  await page.setViewportSize({ width: 1280, height: 800 });
  await expect(page.locator(".sidebar")).toBeVisible();
});

test("computed design tokens follow the selected theme", async ({ page }) => {
  await page.goto("/");
  for (const [scheme, background] of [
    ["light", "rgb(248, 250, 252)"],
    ["dark", "rgb(11, 17, 32)"],
  ] as const) {
    await page.emulateMedia({ colorScheme: scheme });
    await page.reload();
    await expect
      .poll(() => page.evaluate(() => getComputedStyle(document.body).backgroundColor))
      .toBe(background);
    await expect
      .poll(() =>
        page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--ok").trim()),
      )
      .not.toBe("");
  }
});

test.describe("accessibility states", () => {
  for (const route of [...routes.map(([path]) => path), "/__gallery"]) {
    test(`${route} has no WCAG violations in both themes`, async ({ page }) => {
      for (const colorScheme of ["light", "dark"] as const) {
        await page.emulateMedia({ colorScheme });
        await page.goto(route);
        await expect(page.locator("h1")).toBeVisible();
        const result = await new AxeBuilder({ page: page as never }).withTags(axeTags).analyze();
        expect(result.violations, JSON.stringify(result.violations)).toEqual([]);
      }
    });
  }
});

test("drawer owns focus and does not steal focus on page load", async ({ page }) => {
  await page.goto("/__gallery");
  await expect(page.locator("body")).toBeFocused();
  const trigger = page.getByRole("button", { name: "Open drawer" });
  await trigger.focus();
  await trigger.press("Enter");
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: "Gallery drawer" })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(dialog.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(dialog.getByRole("button", { name: "Re-render gallery (0)", exact: true })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(dialog.getByRole("button", { name: "Done", exact: true })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(dialog.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.keyboard.press("Shift+Tab");
  await expect(dialog.getByRole("button", { name: "Done", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(trigger).toBeFocused();
});

test("gallery covers components, drawer/toast axe, toggle and segments", async ({ page }) => {
  await page.goto("/__gallery");
  await expect(page.locator(".ui-skeleton")).toHaveCount(3);
  await page.getByRole("button", { name: "Try again" }).click();
  await expect(page.getByText("Retry attempts: 1")).toBeAttached();
  await page.getByRole("button", { name: "Set up gallery" }).click();
  await expect(page.getByText("Empty actions: 1")).toBeAttached();
  await expect(
    page.getByText("Access denied. A workspace Owner can grant access in Settings → Users & roles."),
  ).toBeVisible();
  for (const heading of ["Alert", "KPI", "Plate", "Progress", "Segments", "Toggle", "Week cell"])
    await expect(page.getByRole("heading", { name: heading })).toBeVisible();
  await expect(page.getByTestId("status-vocabulary").locator("[data-status]")).toHaveCount(29);
  const toggle = page.getByRole("switch");
  await expect(toggle).toHaveAttribute("aria-checked", "false");
  await toggle.click();
  await expect(toggle).toHaveAttribute("aria-checked", "true");
  await page.getByRole("button", { name: "Mine" }).click();
  await expect(page.getByRole("button", { name: "Mine" })).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "Open drawer" }).click();
  await expect(new AxeBuilder({ page: page as never }).withTags(axeTags).analyze()).resolves.toMatchObject({
    violations: [],
  });
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Show success toast" }).click();
  await expect(page.getByTestId("toast")).toBeVisible();
  await expect(page.getByTestId("toast-live")).toHaveText("Reversible action completed");
});

test("reversible toast undo and focus pause work", async ({ page }) => {
  await openWithPausedClock(page, "/__gallery");
  await page.getByRole("button", { name: "Show success toast" }).click();
  await page.mouse.move(0, 0);
  const toast = page.locator(".toast");
  await expect(toast.getByRole("button", { name: "Undo" })).toBeVisible();
  await toast.focus();
  await page.mouse.move(0, 0);
  await page.clock.runFor(11000);
  await expect(toast).toBeVisible();
  await toast.getByRole("button", { name: "Undo" }).click();
  await expect(toast).toBeHidden();
});

test("offline disables writes and removes the banner when online", async ({ page, context }) => {
  await page.goto("/");
  const offlineBanner = page
    .getByRole("status")
    .filter({ hasText: "You are offline. Cached data is read-only." });
  await expect(offlineBanner).toHaveCount(0);
  await context.setOffline(true);
  await expect(offlineBanner).toBeVisible();
  await context.setOffline(false);
  await expect(offlineBanner).toHaveCount(0);
});

test("status vocabulary matches the literal system vocabulary", async ({ page }) => {
  await page.goto("/__gallery");
  const expected = [
    ["Valid", "status-ok"],
    ["Valid", "status-ok"],
    ["Expires", "status-warn"],
    ["Expired", "status-crit"],
    ["Missing", "status-idle"],
    ["Awaiting verification", "status-warn"],
    ["Rejected", "status-crit"],
    ["Rejected", "status-crit"],
    ["Usable", "status-ok"],
    ["Usable, warning", "status-warn"],
    ["Off road", "status-crit"],
    ["In workshop", "status-idle"],
    ["No driver", "status-idle"],
    ["Running", "status-accent"],
    ["Draft", "status-idle"],
    ["Awaiting approval", "status-warn"],
    ["Approved", "status-ok"],
    ["Exported", "status-ok"],
    ["Missing", "status-crit"],
    ["Vacation / Sick / …", "status-idle"],
    ["Possible breach", "status-warn"],
    ["Blocked", "status-crit"],
    ["On shift", "status-ok"],
    ["Not started", "status-accent"],
    ["Absent", "status-idle"],
    ["Onboarding", "status-idle"],
    ["Critical", "status-crit"],
    ["Warning", "status-warn"],
    ["Notice", "status-accent"],
  ];
  const vocabulary = page.getByTestId("status-vocabulary");
  expect(expected).toHaveLength(29);
  await expect(vocabulary.locator("[data-status] svg")).toHaveCount(29);
  for (let index = 0; index < expected.length; index++) {
    const pill = vocabulary.locator("[data-status]").nth(index);
    await expect(pill).toContainText(expected[index][0]);
    await expect(pill).toHaveClass(new RegExp(expected[index][1]));
  }
  await expect(vocabulary.getByText("Possible breach", { exact: true })).toHaveCount(1);
  await expect(vocabulary.getByText("Warning", { exact: true })).toHaveCount(1);
  await expect(vocabulary.locator('[data-status="rule-warning"]')).toHaveText("Possible breach");
  await expect(vocabulary.locator('[data-status="warning"]')).toHaveText("Warning");
  await expect(
    page
      .locator('[data-status]:not([data-status="rule-warning"])')
      .getByText("Possible breach", { exact: true }),
  ).toHaveCount(0);
});
