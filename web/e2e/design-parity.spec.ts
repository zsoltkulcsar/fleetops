import { test, expect } from "./fixtures";
import type { components } from "@fleetops/api-client";

test("office queue preserves responsive layout and readable theme tokens", async ({ page }, testInfo) => {
  const queue: components["schemas"]["AttentionResult"] = {
    companyEmpty: false,
    totals: { open: 3, critical: 2 },
    groups: [
      {
        subjectType: "vehicle",
        subjectId: "vehicle-1",
        subjectName: "KS-DC 1042",
        worstSeverity: "critical",
        items: [
          {
            id: "alert-1",
            ruleId: "A-05",
            severity: "critical",
            scopeKey: "hu",
            dueAt: "2026-11-02",
            status: "open",
            snoozable: false,
            version: 1,
          },
        ],
      },
      {
        subjectType: "employee",
        subjectId: "employee-1",
        subjectName: "Jens Hartmann",
        worstSeverity: "critical",
        items: [
          {
            id: "alert-2",
            ruleId: "A-02",
            severity: "critical",
            scopeKey: "driver_card",
            dueAt: "2026-11-03",
            status: "open",
            snoozable: false,
            version: 1,
          },
        ],
      },
      {
        subjectType: "vehicle",
        subjectId: "vehicle-2",
        subjectName: "KS-DC 1177",
        worstSeverity: "warning",
        items: [
          {
            id: "alert-3",
            ruleId: "A-03",
            severity: "warning",
            scopeKey: "registration",
            dueAt: "2026-11-20",
            status: "open",
            snoozable: true,
            version: 1,
          },
        ],
      },
    ],
  };
  await page.route("**/api/v1/attention", (route) => route.fulfill({ json: queue }));
  await page.route("**/api/v1/documents/counts", (route) =>
    route.fulfill({ json: { queue: 12, expiring: 1, expired: 2, missing: 0 } }),
  );
  await page.route("**/api/v1/notifications?*", (route) => route.fulfill({ json: { unread: 2, items: [] } }));
  await page.route("**/api/v1/readiness/summary", (route) =>
    route.fulfill({ json: { vehiclesUsable: 18, vehiclesTotal: 20 } }),
  );
  for (const colorScheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme });
    await page.goto("/");
    await expect(page.locator(".attention-card")).toHaveCount(3);
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: testInfo.outputPath(`attention-${colorScheme}.png`), fullPage: true });
    const sizes = await page.evaluate(() => ({
      page: document.documentElement.scrollWidth,
      viewport: innerWidth,
    }));
    expect(sizes.page).toBeLessThanOrEqual(sizes.viewport);
    await expect(page.locator("#office-search")).toBeInViewport();
    await expect(page.getByRole("button", { name: "Sign out" })).toBeInViewport();
    const action = page.locator(".attention-card .ui-button.primary").first();
    const box = await action.boundingBox();
    expect(box?.height).toBeGreaterThanOrEqual(32);
    await expect(action).toHaveCSS("border-radius", "6px");
    await expect(page.locator(".attention-toolbar")).toHaveCSS("border-top-left-radius", "8px");
    const badge = page.locator(".nav-badge").first();
    if (await badge.isVisible()) {
      const colors = await badge.evaluate((el) => ({
        foreground: getComputedStyle(el).color,
        intended: getComputedStyle(el).getPropertyValue("--accent-ink").trim(),
      }));
      expect(colors.foreground).toBe(colorScheme === "dark" ? "rgb(14, 20, 27)" : "rgb(255, 255, 255)");
    }
  }
});
