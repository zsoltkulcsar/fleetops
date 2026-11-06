import { test, expect } from "./fixtures";

test("app root is inert only while the drawer is open", async ({ page }) => {
  await page.goto("/__gallery");
  const app = page.locator("#app");
  await expect(app).not.toHaveAttribute("inert", "");
  await page.getByRole("button", { name: "Open drawer" }).click();
  await expect(app).toHaveAttribute("inert", "");
  await page.keyboard.press("Escape");
  await expect(app).not.toHaveAttribute("inert", "");
});

test("shift tab straight after opening stays inside the drawer", async ({ page }) => {
  await page.goto("/__gallery");
  await page.getByRole("button", { name: "Open drawer" }).click();
  const dialog = page.getByRole("dialog");
  await page.keyboard.press("Shift+Tab");
  await expect(dialog.getByRole("button", { name: "Done" })).toBeFocused();
});

test("a parent re-render keeps focus inside the drawer", async ({ page }) => {
  await page.goto("/__gallery");
  await page.getByRole("button", { name: "Open drawer" }).click();
  const rerender = page.getByRole("button", { name: /Re-render gallery/ });
  await rerender.focus();
  await rerender.click();
  await expect(rerender).toBeFocused();
});

test("first load on a real route leaves focus on body", async ({ page }) => {
  await page.goto("/fleet");
  await expect(page.locator("body")).toBeFocused();
});
test("first tab on a real route reaches the skip link", async ({ page }) => {
  await page.goto("/fleet");
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Skip to main content" })).toBeFocused();
});
test("skip link moves focus to main", async ({ page }) => {
  await page.goto("/fleet");
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page.keyboard.press("Tab");
  await page.keyboard.press("Enter");
  await expect(page.locator("#main-content")).toBeFocused();
});
test("in-app navigation moves focus to the page heading", async ({ page }) => {
  await page.goto("/fleet");
  if (page.viewportSize()!.width < 768) {
    await page.getByRole("button", { name: "More" }).click();
    await page.getByRole("link", { name: "Documents", exact: true }).click();
  } else {
    await page.getByRole("link", { name: "Documents", exact: true }).click();
  }
  await expect(page.getByRole("heading", { level: 1, name: "Documents" })).toBeFocused();
});
