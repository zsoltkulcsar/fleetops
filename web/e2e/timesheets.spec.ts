import { test, expect } from "./fixtures";
import AxeBuilder from "@axe-core/playwright";
import { detail, openShift, shift, timesheetRoutes, week, type Schema } from "./timesheets-helpers";

test("week grid shows shifts, absences, missing days and warnings with icon and text", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await timesheetRoutes(page);
  await page.goto("/timesheets");
  const grid = page.getByRole("table", { name: "Driver week grid" });
  await expect(grid.getByRole("columnheader", { name: /Mon/ })).toContainText("09.11.");
  await expect(grid.getByRole("columnheader", { name: /Sun/ })).toContainText("15.11.");
  for (const [index, text] of [
    [0, "8:00"],
    [1, "Vacation"],
    [2, "Missing"],
  ] as const) {
    const cell = grid.locator(".ts-cell").nth(index);
    await expect(cell).toContainText(text);
    await expect(cell.locator("svg").first()).toBeAttached();
  }
  await expect(grid.locator(".ts-cell").first()).toContainText("Awaiting");
  await expect(grid.locator(".ts-cell").first()).toContainText("Warning");
  await expect(grid.locator(".ts-cell").first().locator("svg")).toHaveCount(2);
  await expect(grid).toContainText("1 (DE)");
});

test("filter chips and search ask the server for the page", async ({ page }) => {
  await timesheetRoutes(page);
  await page.goto("/timesheets");
  for (const filter of ["awaiting", "missing", "warnings", "all"]) {
    const request = page.waitForRequest(
      (request) =>
        request.url().includes("/timesheets/week?") &&
        new URL(request.url()).searchParams.get("filter") === filter,
    );
    await page.getByRole("button", { name: new RegExp(`^${filter} `, "i") }).click();
    expect(new URL((await request).url()).searchParams.get("page")).toBe("1");
    await expect(page.getByRole("button", { name: "Dana Driver", exact: true })).toBeVisible();
  }
  const searched = page.waitForRequest(
    (request) => new URL(request.url()).searchParams.get("search") === "D-100",
  );
  await page.getByLabel("Search driver").fill("D-100");
  expect(new URL((await searched).url()).searchParams.get("pageSize")).toBe("50");
  const next = page.waitForRequest(
    (request) =>
      request.url().includes("/timesheets/week?") && new URL(request.url()).searchParams.get("page") === "2",
  );
  await page.getByRole("button", { name: "Next", exact: true }).click();
  expect(new URL((await next).url()).searchParams.get("search")).toBe("D-100");
});

test("keyboard shortcuts move, open, approve and can be turned off", async ({ page }) => {
  const grid = week();
  grid.rows.push({
    ...structuredClone(grid.rows[0]),
    employeeId: "00000000-0000-0000-0000-000000000032",
    driver: "Ralf Driver",
  });
  await timesheetRoutes(page, grid);
  await page.goto("/timesheets");
  await page.keyboard.press("/");
  await expect(page.getByLabel("Search driver")).toBeFocused();
  await page.getByRole("table").focus();
  await page.keyboard.press("j");
  await expect(page.getByRole("button", { name: "Dana Driver", exact: true })).toBeFocused();
  await page.keyboard.press("j");
  await expect(page.getByRole("button", { name: "Ralf Driver", exact: true })).toBeFocused();
  await page.keyboard.press("k");
  await expect(page.getByRole("button", { name: "Dana Driver", exact: true })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.getByRole("button", { name: "Dana Driver", exact: true }).focus();
  await page.keyboard.press("a");
  await expect(page.getByRole("dialog", { name: "Approve shifts" })).toBeVisible();
  await page.keyboard.press("Escape");
  await page.getByLabel("Keyboard shortcuts").uncheck();
  await page.getByRole("table").focus();
  for (const key of ["/", "j", "k", "a", "Enter"]) await page.keyboard.press(key);
  await expect(page.getByRole("table")).toBeFocused();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.reload();
  await expect(page.getByLabel("Keyboard shortcuts")).not.toBeChecked();
});

test("shortcuts do not fire while typing in search", async ({ page }) => {
  await timesheetRoutes(page);
  await page.goto("/timesheets");
  const search = page.getByLabel("Search driver");
  await search.fill("");
  await search.pressSequentially("jka/");
  await search.press("Enter");
  await search.press("Escape");
  await expect(search).toHaveValue("jka/");
  await expect(search).toBeFocused();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.locator("tr[data-focused='true']")).toHaveCount(0);
});

test("bulk approve shows a persistent banner and retries only the failed shifts", async ({ page }) => {
  const grid = week();
  const failedId = "00000000-0000-0000-0000-000000000065";
  grid.rows[0].days[0].shifts.push({ id: failedId, version: 4, status: "submitted", flagged: false });
  await timesheetRoutes(page, grid);
  await page.route(`**/api/v1/timesheets/shifts/${failedId}`, (route) =>
    route.fulfill({ json: detail({ id: failedId, version: 5 }) }),
  );
  const requests: Schema["BulkApprovalRequest"][] = [];
  await page.route("**/api/v1/timesheets/approve", (route) => {
    requests.push(route.request().postDataJSON());
    const result: Schema["BulkApprovalResult"] =
      requests.length === 1
        ? {
            approved: 1,
            failed: 1,
            results: [
              { shiftId: shift.id, status: "approved", code: null },
              { shiftId: failedId, status: "failed", code: "version_changed" },
            ],
          }
        : { approved: 1, failed: 0, results: [{ shiftId: failedId, status: "approved", code: null }] };
    return route.fulfill({ json: result });
  });
  await page.goto("/timesheets");
  await page.getByLabel("Select Dana Driver", { exact: true }).check();
  await page.getByRole("button", { name: "Approve selected", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Approve", exact: true }).click();
  const banner = page.getByText("Approved 1. 1 could not be approved.", { exact: true });
  await expect(banner).toBeVisible();
  await page.getByRole("button", { name: "Next week" }).click();
  await expect(banner).toBeVisible();
  await page.getByRole("button", { name: "Retry failed", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Approve", exact: true }).click();
  await expect(page.getByText("Approved 1. 0 could not be approved.", { exact: true })).toBeVisible();
  expect(requests[0].items.map((item) => item.shiftId)).toEqual([shift.id, failedId]);
  expect(requests[1].items).toEqual([{ shiftId: failedId, version: 5, note: null }]);
});

test("a flagged shift asks for a note before approval", async ({ page }) => {
  const grid = week();
  grid.rows[0].days[0].shifts[0].flagged = true;
  await timesheetRoutes(page, grid);
  let body: Schema["BulkApprovalRequest"] | undefined;
  await page.route("**/api/v1/timesheets/approve", (route) => {
    body = route.request().postDataJSON();
    return route.fulfill({
      json: {
        approved: 1,
        failed: 0,
        results: [{ shiftId: shift.id, status: "approved", code: null }],
      } satisfies Schema["BulkApprovalResult"],
    });
  });
  await page.goto("/timesheets");
  await page.getByLabel("Select Dana Driver", { exact: true }).check();
  await page.getByRole("button", { name: "Approve selected", exact: true }).click();
  const approve = page.getByRole("dialog").getByRole("button", { name: "Approve", exact: true });
  await expect(approve).toBeDisabled();
  expect(body).toBeUndefined();
  await page.getByLabel(`Warning acknowledgement for shift ${shift.id}`).fill("Checked the work record");
  await approve.click();
  await expect(page.getByText("Approved 1. 0 could not be approved.", { exact: true })).toBeVisible();
  expect(body?.items).toEqual([{ shiftId: shift.id, version: 3, note: "Checked the work record" }]);
});

test("shift drawer shows activities, nights, flags and history", async ({ page }) => {
  const current = detail();
  current.flags = ["deviceTimeGap"];
  await timesheetRoutes(page, week(), current);
  await openShift(page);
  const drawer = page.getByRole("dialog");
  await expect(drawer.locator(".ts-timeline")).toContainText("Other work");
  await expect(drawer.locator(".ts-timeline")).toContainText("09.11.2026 09:00");
  await expect(drawer.locator(".ts-timeline")).toContainText("8:00");
  await expect(drawer.getByText("09.11.2026 \u00B7 DE \u00B7 Cab", { exact: true })).toBeVisible();
  await expect(
    drawer.getByText("Phone clock differed from server time by more than 5 min", { exact: false }),
  ).toBeVisible();
  await expect(drawer).toContainText("Helen Office");
  await expect(drawer).toContainText("Reason: Corrected end from work record");
  await expect(drawer).toContainText("Shift end: 09.11.2026 16:00 \u2192 09.11.2026 17:00");
});

test("reject needs a reason", async ({ page }) => {
  const current = detail();
  await timesheetRoutes(page, week(), current);
  let body: Schema["RejectShiftRequest"] | undefined;
  await page.route(`**/api/v1/timesheets/shifts/${shift.id}/reject`, (route) => {
    body = route.request().postDataJSON();
    current.shift.status = "rejected";
    current.shift.rejectReason = body!.reason;
    return route.fulfill({
      json: { shiftId: shift.id, version: 4, status: "rejected" } satisfies Schema["OfficeShiftResult"],
    });
  });
  await openShift(page);
  await page.getByRole("button", { name: "Reject -", exact: true }).click();
  await page.getByRole("button", { name: "Reject and notify driver" }).click();
  expect(
    await page
      .getByLabel("Reason", { exact: true })
      .evaluate((element: HTMLTextAreaElement) => element.validity.valueMissing),
  ).toBe(true);
  expect(body).toBeUndefined();
  await page.getByLabel("Reason", { exact: true }).fill("Correct the break");
  await page.getByRole("button", { name: "Reject and notify driver" }).click();
  await expect(page.getByRole("dialog")).toContainText(
    "Rejected: Correct the break. The driver can fix and resubmit.",
  );
  expect(body).toEqual({ version: 3, reason: "Correct the break" });
});

test("office edit needs a reason and returns an approved shift to awaiting", async ({ page }) => {
  const current = detail({ status: "approved" });
  await timesheetRoutes(page, week(), current);
  let body: Schema["EditShiftRequest"] | undefined;
  await page.route(`**/api/v1/timesheets/shifts/${shift.id}`, (route) => {
    if (route.request().method() !== "PUT") return route.fulfill({ json: current });
    body = route.request().postDataJSON();
    current.shift = {
      ...current.shift,
      ...body!.input,
      activities: body!.input.activities!,
      overnights: body!.input.overnights!,
      status: "submitted",
      version: 4,
    };
    return route.fulfill({
      json: { shiftId: shift.id, version: 4, status: "submitted" } satisfies Schema["OfficeShiftResult"],
    });
  });
  await openShift(page);
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  await expect(page.getByText(/Saving returns this approved shift to awaiting approval\./)).toBeVisible();
  await expect(page.getByRole("button", { name: "Save shift", exact: true })).toBeDisabled();
  expect(body).toBeUndefined();
  await page.getByLabel("End odometer (km)").fill("220");
  await page.getByLabel("Reason", { exact: true }).fill("Corrected mileage");
  await page.getByRole("button", { name: "Save shift", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Edit shift", exact: true })).toHaveCount(0);
  await expect(page.getByRole("dialog")).toContainText("Awaiting");
  expect(body?.reason).toBe("Corrected mileage");
  expect(body?.input.odometerEndKm).toBe(220);
});

test("add shift on a missing day", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await timesheetRoutes(page);
  let body: Schema["AddShiftRequest"] | undefined;
  await page.route("**/api/v1/timesheets/shifts", (route) => {
    body = route.request().postDataJSON();
    return route.fulfill({
      json: { shiftId: shift.id, version: 1, status: "submitted" } satisfies Schema["OfficeShiftResult"],
    });
  });
  await page.goto("/timesheets");
  await page.locator(".ts-cell").nth(2).click();
  await expect(page.getByRole("dialog", { name: "Add shift", exact: true })).toBeVisible();
  await expect(page.getByLabel("Date", { exact: true })).toHaveValue("2026-11-11");
  await expect(page.getByLabel("Driver", { exact: true })).toHaveValue(detail().employeeId);
  await page.getByLabel("Start \u00B7 Europe/Berlin", { exact: true }).fill("2026-11-11T09:00");
  await page.getByLabel("End \u00B7 Europe/Berlin", { exact: true }).fill("2026-11-11T17:00");
  await page.getByLabel("Reason", { exact: true }).fill("Driver could not record shift");
  await page.getByRole("button", { name: "Save shift", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(body).toMatchObject({
    employeeId: detail().employeeId,
    reason: "Driver could not record shift",
    input: {
      date: "2026-11-11",
      startAt: "2026-11-11T08:00:00.000Z",
      endAt: "2026-11-11T16:00:00.000Z",
      activities: [
        { type: "other_work", startAt: "2026-11-11T08:00:00.000Z", endAt: "2026-11-11T16:00:00.000Z" },
      ],
    },
  });
});

test("office ends a running shift", async ({ page }) => {
  const current = detail({
    status: "running",
    endAt: null,
    activities: [{ ...shift.activities[0], endAt: null }],
  });
  await timesheetRoutes(page, week(), current);
  let body: Schema["EndShiftRequest"] | undefined;
  await page.route(`**/api/v1/timesheets/shifts/${shift.id}/end`, (route) => {
    body = route.request().postDataJSON();
    current.shift.status = "draft";
    current.shift.endAt = body!.endAt;
    current.shift.activities[0].endAt = body!.endAt;
    return route.fulfill({
      json: { shiftId: shift.id, version: 4, status: "draft" } satisfies Schema["OfficeShiftResult"],
    });
  });
  await openShift(page);
  await page.getByRole("button", { name: "End shift -", exact: true }).click();
  await page.getByLabel("End \u00B7 Europe/Berlin", { exact: true }).fill("2026-11-09T17:00");
  await page.getByLabel("Reason", { exact: true }).fill("Driver phone unavailable");
  await page.getByRole("button", { name: "End shift and notify driver" }).click();
  await expect(page.getByRole("heading", { name: "End running shift" })).toHaveCount(0);
  await expect(page.getByRole("dialog")).toContainText("Draft");
  await expect(page.locator(".ts-timeline")).toContainText("09.11.2026 17:00");
  expect(body).toEqual({
    version: 3,
    reason: "Driver phone unavailable",
    endAt: "2026-11-09T16:00:00.000Z",
    odometerEndKm: null,
  });
});

test("payroll cutoff banner shows near the cutoff", async ({ page }) => {
  const grid = week();
  grid.cutoff = { date: "2026-11-12", awaitingShifts: 3 };
  await timesheetRoutes(page, grid);
  await page.goto("/timesheets");
  await expect(
    page.getByText("Payroll cutoff 12.11.2026 — 3 shifts still awaiting approval.", { exact: true }),
  ).toBeVisible();
});

test("timesheets shows an error with try again when the week fails to load", async ({ page }) => {
  await timesheetRoutes(page);
  let unavailable = true;
  await page.route("**/api/v1/timesheets/week?*", (route) =>
    unavailable ? route.fulfill({ status: 503 }) : route.fulfill({ json: week() }),
  );
  await page.goto("/timesheets");
  await expect(
    page.getByText("Data could not be loaded. Check the connection and try again.", { exact: true }),
  ).toBeVisible();
  unavailable = false;
  await page.getByRole("button", { name: "Try again", exact: true }).click();
  await expect(page.getByRole("button", { name: "Dana Driver", exact: true })).toBeVisible();
  await expect(
    page.getByText("Data could not be loaded. Check the connection and try again.", { exact: true }),
  ).toHaveCount(0);
});

test("timesheets has no accessibility violations", async ({ page }) => {
  await timesheetRoutes(page);
  await page.goto("/timesheets");
  await expect(page.getByRole("button", { name: "Dana Driver", exact: true })).toBeVisible();
  expect(
    (await new AxeBuilder({ page: page as unknown as import("playwright-core").Page }).analyze()).violations,
  ).toEqual([]);
});
