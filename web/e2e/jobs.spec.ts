import { test, expect } from "./fixtures";
import type { components } from "@fleetops/api-client";

const jobs: components["schemas"]["JobListResult"] = {
  items: [
    {
      name: "file-orphans",
      startedAt: "2026-11-06T10:00:00Z",
      finishedAt: "2026-11-06T10:00:01Z",
      status: "Succeeded",
      processed: 2,
    },
  ],
};

test("owner sees timed jobs when the trigger is on", async ({ page }) => {
  await page.route("**/api/v1/admin/jobs", (route) => route.fulfill({ status: 200, json: jobs }));
  await page.goto("/settings");
  await expect(page.getByRole("heading", { name: "Timed jobs" })).toBeVisible();
  await expect(page.getByText("file-orphans")).toBeVisible();
});

test("run now calls the trigger and shows the result", async ({ page }) => {
  let triggered = false;
  const result: components["schemas"]["JobRunResult"] = {
    status: "succeeded",
    processed: 2,
    errorType: null,
  };
  await page.route("**/api/v1/admin/jobs", (route) => route.fulfill({ status: 200, json: jobs }));
  await page.route("**/api/v1/admin/jobs/file-orphans/run", (route) => {
    triggered = true;
    return route.fulfill({ status: 200, json: result });
  });
  await page.goto("/settings");
  await page.getByRole("button", { name: "Run now" }).click();
  await expect.poll(() => triggered).toBe(true);
  await expect(page.getByText("succeeded, 2 processed")).toBeVisible();
});

test("run now failure says so", async ({ page }) => {
  await page.route("**/api/v1/admin/jobs", (route) => route.fulfill({ status: 200, json: jobs }));
  await page.route("**/api/v1/admin/jobs/file-orphans/run", (route) => route.fulfill({ status: 500 }));
  await page.goto("/settings");
  await page.getByRole("button", { name: "Run now" }).click();
  await expect(page.getByText("Could not run the job.")).toBeVisible();
});

test("timed jobs panel is hidden when the trigger is off", async ({ page }) => {
  await page.route("**/api/v1/admin/jobs", (route) =>
    route.fulfill({ status: 404, json: { code: "not_found" } }),
  );
  await page.goto("/settings");
  await expect(page.getByRole("heading", { name: "Timed jobs" })).toHaveCount(0);
});
