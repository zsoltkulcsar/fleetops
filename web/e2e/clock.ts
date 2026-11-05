import type { Page } from "@playwright/test";

export async function openWithPausedClock(page: Page, url: string) {
  const start = new Date("2026-11-05T09:00:00Z");
  await page.clock.install({ time: start });
  await page.goto(url);
  await page.clock.pauseAt(new Date(start.getTime() + 60_000));
}
