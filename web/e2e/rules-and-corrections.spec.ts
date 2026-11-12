import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import { driverRoutes, enrolDriver } from "./driver-helpers";
import { shift } from "./timesheets-helpers";

type Schema = components["schemas"];
const employeeId = "00000000-0000-0000-0000-000000000031";
const rules: Schema["ComplianceRuleView"][] = [
  {
    id: "C-04",
    name: "Break",
    guideValue: "Guide",
    value: JSON.stringify({
      drivingMinutes: 120,
      breakMinutes: 45,
      splitFirstMinutes: 15,
      splitLastMinutes: 30,
    }),
    confirmed: false,
    valueSchema: "{}",
  },
  {
    id: "C-01",
    name: "Daily driving",
    guideValue: "Guide",
    value: JSON.stringify({ minutes: 480, extendedMinutes: 540, extensionsPerWeek: 2 }),
    confirmed: false,
    valueSchema: "{}",
  },
];
function snapshot(current: Schema["ShiftView"][] = []): Schema["ShiftSnapshot"] {
  return {
    generatedAt: "2026-11-10T10:00:00Z",
    companyTimeZone: "Europe/Berlin",
    shifts: current,
    appliedCommandIds: [],
    trucks: [],
    suggestedTruckId: null,
    conflicts: [],
    assignments: [],
    complianceRules: rules,
    corrections: [],
  };
}
async function setup(page: Page, initial: Schema["ShiftSnapshot"]) {
  await page.clock.install({ time: new Date("2026-11-10T10:00:00Z") });
  await driverRoutes(page);
  await page.route("**/api/v1/driver/shifts", (route) => route.fulfill({ json: initial }));
  await enrolDriver(page);
}
const current: Schema["ShiftView"] = { ...shift, blockedAtStart: null, deviceTimeGap: false };

test.describe("Driver rule guidance and corrections", () => {
  test.use({ baseURL: "http://127.0.0.1:4175/driver/", viewport: { width: 390, height: 844 } });
  test("driver sees break countdown offline with guide value label", async ({ page, context }) => {
    await setup(
      page,
      snapshot([
        {
          ...current,
          date: "2026-11-10",
          startAt: "2026-11-10T09:00:00Z",
          endAt: null,
          status: "running",
          activities: [
            { id: shift.activities[0].id, type: "driving", startAt: "2026-11-10T09:00:00Z", endAt: null },
          ],
        },
      ]),
    );
    const guidance = page.getByRole("region", { name: "Driving guidance" });
    await expect(guidance).toContainText("Break due in 1:00");
    await expect(guidance).toContainText("Driving today 1:00 of 8:00");
    // The phone must hold the rule values before the signal goes; from here the countdown runs offline.
    await context.setOffline(true);
    await expect(
      guidance.getByText("Guide value \u00b7 rule not yet confirmed", { exact: true }),
    ).toHaveCount(2);
    await page.clock.fastForward(60000);
    await expect(guidance).toContainText("Break due in 0:59");
  });

  test("driver requests a correction and sees the difference", async ({ page }) => {
    const initial = snapshot([current]);
    await setup(page, initial);
    let command: Schema["DriverCommand"] | undefined;
    await page.route("**/api/v1/driver/commands/batch", (route) => {
      const batch: Schema["DriverCommandBatch"] = route.request().postDataJSON();
      command = batch.commands.find((entry) => entry.type === "correction.request");
      if (command)
        initial.corrections = [
          {
            id: command.aggregateId,
            employeeId,
            shiftId: shift.id,
            date: shift.date,
            reason: "wrong_times",
            proposedStartAt: null,
            proposedEndAt: null,
            note: "Check end",
            status: "done",
            requiresHr: false,
            declineReason: null,
            diffs: [{ field: "endAt", before: "16:00", after: "17:00" }],
            requestedAt: initial.generatedAt,
            decidedAt: initial.generatedAt,
          },
        ];
      return route.fulfill({
        json: {
          results: batch.commands.map((entry) => ({
            commandId: entry.commandId,
            status: "applied",
            code: null,
            shift: null,
          })),
        },
      });
    });
    await page.getByRole("link", { name: "Week", exact: true }).click();
    await page.locator(".driver-shift-list li").first().getByRole("button").first().click();
    await page.getByRole("button", { name: "Request correction", exact: true }).click();
    await page.getByLabel("Reason", { exact: true }).selectOption("wrong_times");
    await page.getByLabel("Note (optional)", { exact: true }).fill("Check end");
    await page.getByRole("button", { name: "Send request", exact: true }).click();
    await expect
      .poll(() => command?.payload)
      .toMatchObject({ shiftId: shift.id, reason: "wrong_times", note: "Check end" });
    await expect(page.getByText("endAt: 16:00 \u2192 17:00", { exact: true }).first()).toBeVisible();
    await expect(page.getByText(/Correction done/).first()).toBeVisible();
  });

  test("driver adds a forgotten day", async ({ page }) => {
    await page.clock.install({ time: new Date("2026-11-10T10:00:00Z") });
    await driverRoutes(page);
    await page.route("**/api/v1/driver/snapshot", (route) =>
      route.fulfill({
        json: {
          employeeId,
          generatedAt: "2026-11-10T10:00:00Z",
          cycles: [
            {
              id: shift.id,
              vehicleId: shift.id,
              vehiclePlate: "B-TEST",
              from: "2026-11-09",
              to: "2026-11-15",
              status: "active",
              source: "manual",
              version: 1,
            },
          ],
          documents: [],
          canDrive: true,
          readinessCauses: [],
        },
      }),
    );
    await page.route("**/api/v1/driver/shifts", (route) => route.fulfill({ json: snapshot() }));
    let command: Schema["DriverCommand"] | undefined;
    await page.route("**/api/v1/driver/commands/batch", (route) => {
      const batch: Schema["DriverCommandBatch"] = route.request().postDataJSON();
      command = batch.commands.find((entry) => entry.type === "correction.request");
      return route.fulfill({
        json: {
          results: batch.commands.map((entry) => ({
            commandId: entry.commandId,
            status: "applied",
            code: null,
            shift: null,
          })),
        },
      });
    });
    await enrolDriver(page);
    await page.getByRole("link", { name: "Week", exact: true }).click();
    await page.getByRole("button", { name: "Add forgotten day", exact: true }).click();
    await page.getByRole("button", { name: "Send request", exact: true }).click();
    await expect
      .poll(() => command?.payload)
      .toMatchObject({ shiftId: null, date: "2026-11-09", reason: "missing_shift" });
  });
});

test("a11 acknowledge needs a note", async ({ page }) => {
  await page.route("**/api/v1/attention", (route) =>
    route.fulfill({
      json: {
        groups: [
          {
            subjectType: "shift",
            subjectId: shift.id,
            subjectName: "Dana Driver",
            worstSeverity: "info",
            items: [
              {
                id: shift.id,
                ruleId: "A-11",
                severity: "info",
                scopeKey: "",
                dueAt: null,
                status: "open",
                snoozable: false,
                version: 1,
              },
            ],
          },
        ],
        totals: { open: 1, critical: 0 },
        companyEmpty: false,
      },
    }),
  );
  let received: unknown;
  await page.route("**/api/v1/alerts/*/acknowledge-rule-warning", (route) => {
    received = route.request().postDataJSON();
    return route.fulfill({ json: { id: shift.id, status: "resolved", version: 2 } });
  });
  await page.goto("/");
  await page.getByRole("button", { name: "Acknowledge", exact: true }).click();
  const note = page.getByLabel("Acknowledgement note", { exact: true });
  await expect(note).toHaveAttribute("required", "");
  const submit = page.getByRole("button", { name: "Acknowledge", exact: true });
  await expect(submit).toBeDisabled();
  await note.fill("   ");
  await expect(submit).toBeDisabled();
  expect(received).toBeUndefined();
  await note.fill("Checked tachograph");
  await submit.click();
  await expect.poll(() => received).toEqual({ version: 1, note: "Checked tachograph" });
});

test("owner confirms a compliance rule", async ({ page }) => {
  let confirmed = false;
  await page.route("**/api/v1/settings", (route) =>
    route.fulfill({ json: { values: {}, releasableDocTypes: [], holidays: [] } }),
  );
  await page.route("**/api/v1/admin/jobs", (route) => route.fulfill({ json: { items: [] } }));
  await page.route("**/api/v1/settings/compliance-rules", (route) =>
    route.fulfill({ json: { rules: [{ ...rules[0], confirmed }], confirmed: confirmed ? 1 : 0, total: 1 } }),
  );
  await page.route("**/api/v1/settings/compliance-rules/C-04/confirm", (route) => {
    expect(route.request().method()).toBe("POST");
    confirmed = true;
    return route.fulfill({ json: { rules: [{ ...rules[0], confirmed }], confirmed: 1, total: 1 } });
  });
  await page.goto("/settings");
  await expect(page.getByText("Guide value \u00b7 rule not yet confirmed", { exact: true })).toBeVisible();
  await expect(page.getByText("0 of 1 confirmed", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(page.getByText("1 of 1 confirmed", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Confirm", exact: true })).toBeDisabled();
});
