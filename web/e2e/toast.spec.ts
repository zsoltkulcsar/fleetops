import { test, expect } from "./fixtures";
import { openWithPausedClock } from "./clock";

test("unpaused toast is visible at 9.9 seconds and gone at 10.1 seconds", async ({ page }) => {
  await openWithPausedClock(page, "/__gallery");
  await page.getByRole("button", { name: "Show success toast" }).click();
  await page.mouse.move(0, 0);
  const toast = page.getByTestId("toast");
  await page.clock.runFor(9900);
  await expect(toast).toBeVisible();
  await page.clock.runFor(200);
  await expect(toast).toBeHidden();
});

test("hover pauses the toast and it closes after the pointer leaves", async ({ page }) => {
  await openWithPausedClock(page, "/__gallery");
  await page.getByRole("button", { name: "Show success toast" }).click();
  await page.mouse.move(0, 0);
  const toast = page.getByTestId("toast");
  await toast.hover();
  await page.clock.runFor(11000);
  await expect(toast).toBeVisible();
  await page.mouse.move(0, 0);
  await page.clock.runFor(10001);
  await expect(toast).toBeHidden();
});

test("a second toast gets a full 10 seconds after the first timed out", async ({ page }) => {
  await openWithPausedClock(page, "/__gallery");
  const show = page.getByRole("button", { name: "Show success toast" });
  await show.click();
  await page.mouse.move(0, 0);
  await page.clock.runFor(10001);
  await expect(page.getByTestId("toast")).toBeHidden();
  await show.click();
  await page.mouse.move(0, 0);
  const toast = page.getByTestId("toast");
  await expect(toast).toBeVisible();
  await page.clock.runFor(9900);
  await expect(toast).toBeVisible();
  await page.clock.runFor(200);
  await expect(toast).toBeHidden();
});

test("a toast closes normally after the previous one was dismissed by mouse", async ({ page }) => {
  await openWithPausedClock(page, "/__gallery");
  const show = page.getByRole("button", { name: "Show success toast" });
  await show.click();
  await page.mouse.move(0, 0);
  await page.getByTestId("toast").getByRole("button", { name: "Dismiss" }).click();
  await show.click();
  await page.mouse.move(0, 0);
  const toast = page.getByTestId("toast");
  await page.clock.runFor(10001);
  await expect(toast).toBeHidden();
});

test("reversible toast shows Undo and calls it", async ({ page }) => {
  await openWithPausedClock(page, "/__gallery");
  await page.getByRole("button", { name: "Show success toast" }).click();
  const toast = page.getByTestId("toast");
  await expect(toast.getByRole("button", { name: "Undo" })).toBeVisible();
  await toast.getByRole("button", { name: "Undo" }).click();
  await expect(toast).toBeHidden();
});
