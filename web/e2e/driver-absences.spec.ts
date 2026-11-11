import { test, expect, type Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import { driverRoutes, enrolDriver } from "./driver-helpers";

type Schema = components["schemas"];
const employeeId = "00000000-0000-0000-0000-000000000031";
const absence: Schema["AbsenceView"] = {
  id: "00000000-0000-0000-0000-000000000081",
  employeeId,
  from: "2026-11-09",
  to: "2026-11-10",
  halfDay: null,
  type: "vacation",
  status: "declined",
  note: null,
  declineReason: "Coverage needed",
  cancelReason: null,
};
const cycle: Schema["DriverCycle"] = {
  id: "00000000-0000-0000-0000-000000000082",
  vehicleId: "00000000-0000-0000-0000-000000000083",
  vehiclePlate: "B-TEST 32",
  from: "2026-11-09",
  to: "2026-11-22",
  status: "planned",
  source: "generated",
  version: 1,
};
const snapshot: Schema["DriverSnapshot"] = {
  employeeId,
  generatedAt: "2026-11-09T10:00:00Z",
  cycles: [cycle],
  documents: [],
  canDrive: true,
  readinessCauses: [],
  holidays: [],
  absences: [absence],
  scheduleRequests: [],
  vacationBalance: { year: 2026, opening: 3, entitlement: 20, used: 2, remaining: 21, left: 21 },
};
async function setup(page: Page, initial = snapshot) {
  await page.clock.setFixedTime(new Date("2026-11-09T10:00:00Z"));
  await driverRoutes(page);
  await page.route("**/api/v1/driver/snapshot", (route) => route.fulfill({ json: initial }));
  await enrolDriver(page);
}

test("driver requests vacation offline and sees waiting to sync", async ({ page, context }) => {
  await setup(page);
  await page.getByRole("link", { name: "Me", exact: true }).click();
  await expect(page.getByText("21 days left", { exact: true })).toBeVisible();
  await context.setOffline(true);
  await page.getByRole("button", { name: "Request vacation", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("From", { exact: true }).fill("2026-11-16");
  await dialog.getByLabel("To", { exact: true }).fill("2026-11-17");
  await dialog.getByRole("button", { name: "Send request", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(
    page.getByRole("status").filter({ hasText: "Vacation request Waiting to sync" }),
  ).toBeVisible();
  await page.getByRole("link", { name: "Week", exact: true }).click();
  await page.getByRole("link", { name: "Me", exact: true }).click();
  await expect(page.getByText("Vacation request Waiting to sync", { exact: true })).toBeVisible();
});

test("driver sees days left and a declined reason", async ({ page }) => {
  await setup(page);
  await page.getByRole("link", { name: "Me", exact: true }).click();
  await expect(page.getByText("21 days left", { exact: true })).toBeVisible();
  const card = page.getByRole("article").filter({ hasText: "Decline reason: Coverage needed" });
  await expect(card).toContainText("Declined");
  await expect(card).toContainText("09.11.2026 to 10.11.2026");
});

test("driver starting on vacation is asked to confirm", async ({ page, context }) => {
  await setup(page, { ...snapshot, absences: [{ ...absence, status: "approved", declineReason: null }] });
  await context.setOffline(true);
  await page.getByRole("button", { name: "Start shift", exact: true }).click();
  const dialog = page.getByRole("dialog");
  const confirmation = dialog.getByRole("checkbox", { name: /Start anyway/ });
  await expect(dialog).toContainText("You're on vacation today. Start anyway?");
  await expect(confirmation).not.toBeChecked();
  await expect(confirmation).toHaveAttribute("required", "");
  await dialog.getByRole("button", { name: "Confirm start", exact: true }).click();
  await expect(dialog).toBeVisible();
  await confirmation.check();
  await dialog.getByRole("button", { name: "Confirm start", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("button", { name: "End shift", exact: true })).toBeVisible();
});

test("driver requests a schedule change and sees it declined with the reason", async ({ page }) => {
  await setup(page);
  let command: Schema["DriverCommand"] | undefined;
  const declined: Schema["ScheduleRequestView"] = {
    id: absence.id,
    employeeId,
    cycleId: cycle.id,
    proposedFrom: "2026-11-10",
    proposedTo: "2026-11-20",
    note: "Family commitment",
    status: "declined",
    declineReason: "Truck needed",
    decidedBy: employeeId,
    decidedAt: "2026-11-09T10:00:00Z",
  };
  await page.route("**/api/v1/driver/commands/batch", (route) => {
    const batch: Schema["DriverCommandBatch"] = route.request().postDataJSON();
    command = batch.commands.find((entry) => entry.type === "schedule.request_change");
    const result: Schema["DriverCommandBatchResult"] = {
      results: batch.commands.map((entry) => ({
        commandId: entry.commandId,
        status: "applied",
        code: null,
        shift: null,
      })),
    };
    return route.fulfill({ json: result });
  });
  await page.getByRole("link", { name: "Week", exact: true }).click();
  await page.getByRole("button", { name: "Request change", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("New from", { exact: true }).fill("2026-11-10");
  await dialog.getByLabel("New to", { exact: true }).fill("2026-11-20");
  await dialog.getByLabel("Note", { exact: true }).fill("Family commitment");
  await dialog.getByRole("button", { name: "Send request", exact: true }).click();
  await expect.poll(() => command?.type).toBe("schedule.request_change");
  expect(command?.payload).toEqual({
    cycleId: cycle.id,
    proposedFrom: "2026-11-10",
    proposedTo: "2026-11-20",
    note: "Family commitment",
  });
  await page.route("**/api/v1/driver/snapshot", (route) =>
    route.fulfill({
      json: {
        ...snapshot,
        scheduleRequests: [{ ...declined, id: command!.aggregateId }],
      } satisfies Schema["DriverSnapshot"],
    }),
  );
  await page.getByRole("button", { name: "Refresh schedule", exact: true }).click();
  await expect(page.getByText(/Declined: Truck needed/)).toBeVisible();
});
