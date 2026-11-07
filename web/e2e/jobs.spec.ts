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

const jobsApiUrl = "http://127.0.0.1:4173/api/v1/admin/jobs";
const runJobApiUrl = "http://127.0.0.1:4173/api/v1/admin/jobs/file-orphans/run";

test("jobs panel calls the exact API path", async ({ page }) => {
  let requestUrl: string | undefined;
  page.on("request", (request) => {
    if (request.url().includes("/api/v1/admin/jobs")) requestUrl = request.url();
  });
  await page.route(jobsApiUrl, (route) => route.fulfill({ status: 200, json: jobs }));
  await page.goto("/settings");
  await expect(page.getByRole("heading", { name: "Timed jobs" })).toBeVisible();
  expect(requestUrl).toBeDefined();
  expect(new URL(requestUrl!).pathname).toBe("/api/v1/admin/jobs");
});

test("owner sees timed jobs when the trigger is on", async ({ page }) => {
  await page.route(jobsApiUrl, (route) => route.fulfill({ status: 200, json: jobs }));
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
  await page.route(jobsApiUrl, (route) => route.fulfill({ status: 200, json: jobs }));
  await page.route(runJobApiUrl, (route) => {
    triggered = true;
    return route.fulfill({ status: 200, json: result });
  });
  await page.goto("/settings");
  await page.getByRole("button", { name: "Run now" }).click();
  await expect.poll(() => triggered).toBe(true);
  await expect(page.getByText("succeeded, 2 processed")).toBeVisible();
});

test("run now failure says so", async ({ page }) => {
  await page.route(jobsApiUrl, (route) => route.fulfill({ status: 200, json: jobs }));
  await page.route(runJobApiUrl, (route) => route.fulfill({ status: 500 }));
  await page.goto("/settings");
  await page.getByRole("button", { name: "Run now" }).click();
  await expect(page.getByText("Could not run the job.")).toBeVisible();
});

test("timed jobs panel is hidden when the trigger is off", async ({ page }) => {
  await page.route(jobsApiUrl, (route) => route.fulfill({ status: 404, json: { code: "not_found" } }));
  await page.goto("/settings");
  await expect(page.getByRole("heading", { name: "Timed jobs" })).toHaveCount(0);
});
