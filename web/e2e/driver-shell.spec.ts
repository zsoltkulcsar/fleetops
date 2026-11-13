import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { driverRoutes, enrolDriver, openEnrol, submitEnrol, setPin, enterPin } from "./driver-helpers";

test.beforeEach(async ({ page }) => driverRoutes(page));

test("bottom nav has four tabs", async ({ page }) => {
  await enrolDriver(page);
  const navigation = page.getByRole("navigation", { name: "Driver navigation" });
  await expect(navigation.getByRole("link")).toHaveText(["Shift", "Week", "Defect", "Me"]);
  for (const name of ["Shift", "Week", "Defect", "Me"]) {
    const tab = navigation.getByRole("link", { name, exact: true });
    await tab.click();
    await expect(
      page.getByRole("heading", { name: name === "Defect" ? "Report defect" : name, exact: true }),
    ).toBeVisible();
    await expect(tab).toHaveAttribute("aria-current", "page");
  }
  const bounds = await navigation.boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.y).toBeGreaterThan(page.viewportSize()!.height * 0.7);
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(page.viewportSize()!.height + 1);
});

async function checkTargets(page: Page) {
  // Only visible targets can be tapped. The screen may still re-render after navigation, removing an element
  // between listing and measuring it, so the whole measurement is retried until the screen is stable.
  await expect(async () => {
    const targets = page.locator("main button, main input, main nav a").filter({ visible: true });
    expect(await targets.count()).toBeGreaterThan(0);
    for (const target of await targets.all()) {
      const bounds = await target.boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.height).toBeGreaterThanOrEqual(48);
      expect(bounds!.width).toBeGreaterThanOrEqual(48);
    }
  }).toPass({ timeout: 10000 });
}

test("targets are at least 48 pixels", async ({ page }) => {
  await openEnrol(page);
  await checkTargets(page);
  await submitEnrol(page);
  await page.getByRole("button", { name: "I acknowledge the privacy notice" }).click();
  await expect(page.getByLabel("Confirm PIN", { exact: true })).toBeVisible();
  await checkTargets(page);
  await setPin(page);
  await page.getByRole("link", { name: "Me", exact: true }).click();
  await checkTargets(page);
});

test("primary button sits in the lower part of the screen", async ({ page }) => {
  await openEnrol(page);
  const button = page.getByRole("button", { name: "Continue", exact: true });
  const bounds = await button.boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.y).toBeGreaterThanOrEqual(page.viewportSize()!.height * 0.65);
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(page.viewportSize()!.height);
});

test("dark theme is used", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await enrolDriver(page);
  await expect(page.locator("html")).toHaveCSS("color-scheme", "dark");
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(11, 17, 32)");
  await expect(page.getByRole("navigation")).toHaveCSS("background-color", "rgb(17, 24, 39)");
});

test("font scale follows the root size", async ({ page }) => {
  await enrolDriver(page);
  const sizes = () =>
    page
      .locator("h1, .driver-greeting, .driver-nav a")
      .evaluateAll((elements) => elements.map((element) => parseFloat(getComputedStyle(element).fontSize)));
  const before = await sizes();
  expect(before).toHaveLength(6);
  await page.evaluate(() => {
    const root = document.documentElement;
    root.style.fontSize = `${parseFloat(getComputedStyle(root).fontSize) * 1.5}px`;
  });
  const after = await sizes();
  after.forEach((size, index) => expect(size / before[index]).toBeCloseTo(1.5, 2));
});

test("offline pill appears", async ({ page, context }) => {
  await enrolDriver(page);
  await expect(page.getByText("Offline", { exact: true })).toHaveCount(0);
  await context.setOffline(true);
  // Schedule loading is also a live status; assert the offline indicator specifically.
  await expect(page.getByRole("status").filter({ hasText: /^Offline$/ })).toHaveText("Offline");
  await context.setOffline(false);
  await expect(page.getByText("Offline", { exact: true })).toHaveCount(0);
});

test("driver pages have no accessibility violations", async ({ page }) => {
  test.setTimeout(60_000);
  const audit = async () => {
    // Axe resolves a different installed playwright-core version, as in fleet.spec.ts.
    const result = await new AxeBuilder({
      page: page as unknown as import("playwright-core").Page,
    })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(result.violations).toEqual([]);
  };
  await page.goto("/driver/");
  await expect(page.getByRole("heading", { name: "Install FleetOps Driver" })).toBeVisible();
  await audit();
  await page.getByRole("button", { name: "Not now" }).click();
  await expect(page.getByLabel("Personnel number")).toBeVisible();
  await audit();
  await submitEnrol(page);
  await expect(page.getByRole("heading", { name: "Privacy notice", exact: true })).toBeVisible();
  await audit();
  await page.getByRole("button", { name: "I acknowledge the privacy notice" }).click();
  await expect(page.getByLabel("Confirm PIN", { exact: true })).toBeVisible();
  await audit();
  await setPin(page);
  for (const name of ["Shift", "Week", "Defect", "Me"]) {
    await page.getByRole("navigation").getByRole("link", { name, exact: true }).click();
    await expect(
      page.getByRole("heading", { name: name === "Defect" ? "Report defect" : name, exact: true }),
    ).toBeVisible();
    await audit();
  }
  await page.reload();
  await expect(page.getByRole("heading", { name: "Enter your PIN" })).toBeVisible();
  await audit();
  await enterPin(page, "111111");
  await expect(page.getByRole("alert")).toHaveText("Wrong PIN. 4 tries left.");
  await audit();
});
