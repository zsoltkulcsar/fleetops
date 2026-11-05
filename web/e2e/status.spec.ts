import { test, expect } from "@playwright/test";

test("dated labels render the exact date with the trailing dot", async ({ page }) => {
  await page.goto("/__gallery");
  await expect(page.locator('[data-status="expiring"]')).toHaveText("Expires 05.11.");
  await expect(page.locator('[data-status="expired"]')).toHaveText("Expired 05.11.");
});
test("no element on the gallery contains the literal DD.MM", async ({ page }) => {
  await page.goto("/__gallery");
  await expect(page.locator("body")).not.toContainText("DD.MM");
});

test("pill colours match the design system in light theme", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" });
  await page.goto("/__gallery");
  for (const [tone, color] of Object.entries({
    ok: "rgb(23, 105, 60)",
    warn: "rgb(138, 86, 0)",
    crit: "rgb(175, 31, 20)",
    idle: "rgb(86, 96, 109)",
    accent: "rgb(27, 78, 145)",
  }))
    await expect
      .poll(() =>
        page
          .locator(`.status-${tone}`)
          .first()
          .evaluate((el) => getComputedStyle(el).color),
      )
      .toBe(color);
});
test("pill colours match the design system in dark theme", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.goto("/__gallery");
  for (const [tone, color] of Object.entries({
    ok: "rgb(98, 203, 140)",
    warn: "rgb(242, 194, 87)",
    crit: "rgb(255, 140, 124)",
    idle: "rgb(167, 178, 190)",
    accent: "rgb(120, 169, 234)",
  }))
    await expect
      .poll(() =>
        page
          .locator(`.status-${tone}`)
          .first()
          .evaluate((el) => getComputedStyle(el).color),
      )
      .toBe(color);
});

test.describe("today rendering", () => {
  test.use({ timezoneId: "America/New_York" });

  test("today renders as DD.MM.YYYY in Berlin across midnight", async ({ page }) => {
    await page.clock.setFixedTime(new Date("2026-11-05T23:30:00Z"));
    await page.goto("/");
    await expect(page.getByTestId("today")).toContainText("06.11.2026");
  });
});
test("primary button keeps 4.5 to 1 contrast on hover", async ({ page }) => {
  for (const theme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme: theme });
    await page.goto("/__gallery");
    const button = page.getByRole("button", { name: "Set up gallery" });
    await button.hover();
    const colors = await button.evaluate((el) => ({
      foreground: getComputedStyle(el).color,
      background: getComputedStyle(el).backgroundColor,
    }));
    expect(colors.foreground).not.toBe(colors.background);
  }
});
