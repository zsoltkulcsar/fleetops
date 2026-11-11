import { test, expect, type Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import { driverRoutes, enrolDriver } from "./driver-helpers";
import { shift } from "./timesheets-helpers";

type Schema = components["schemas"];
const truckId = "00000000-0000-0000-0000-000000000030";
const photoId = "00000000-0000-0000-0000-000000000090";
const photoPath = "e2e/fixtures/defect.png";

async function setup(page: Page) {
  await page.clock.install({ time: new Date("2026-11-10T10:00:00Z") });
  await page.clock.pauseAt(new Date("2026-11-10T10:00:01Z"));
  await driverRoutes(page);
  const snapshot: Schema["ShiftSnapshot"] = {
    generatedAt: "2026-11-10T10:00:00Z",
    companyTimeZone: "Europe/Berlin",
    shifts: [
      { ...shift, truckId, status: "running", endAt: null, blockedAtStart: null, deviceTimeGap: false },
    ],
    appliedCommandIds: [],
    trucks: [
      {
        id: truckId,
        plate: "B-TEST 32",
        kind: "truck",
        usable: true,
        reason: null,
        licenceClassWarning: false,
      },
    ],
    suggestedTruckId: null,
    conflicts: [],
    assignments: [],
  };
  await page.route("**/api/v1/driver/shifts", (route) => route.fulfill({ json: snapshot }));
  const commands: Schema["DriverCommand"][] = [];
  let refuseCancel = false;
  await page.route("**/api/v1/driver/commands/batch", (route) => {
    const body: Schema["DriverCommandBatch"] = route.request().postDataJSON();
    commands.push(...body.commands);
    const result: Schema["DriverCommandBatchResult"] = {
      results: body.commands.map((command) => ({
        commandId: command.commandId,
        status: refuseCancel && command.type === "defect.cancel" ? "failed" : "applied",
        code: refuseCancel && command.type === "defect.cancel" ? "cancel_window_passed" : null,
        shift: null,
      })),
    };
    return route.fulfill({ json: result });
  });
  await enrolDriver(page);
  await page.getByRole("link", { name: "Defect", exact: true }).click();
  await expect(page.getByLabel("Vehicle", { exact: true })).toHaveValue(truckId);
  return {
    commands,
    refuse: () => {
      refuseCancel = true;
    },
  };
}
async function critical(page: Page) {
  await page.getByRole("button", { name: "Do not drive", exact: true }).click();
  await page.getByLabel("Note (optional)").fill("Brake pressure lost");
  await page.getByRole("button", { name: "Send report", exact: true }).click();
  await expect(page.getByText("Sent. Dispatch will confirm.", { exact: true })).toBeVisible();
}

test("report defaults to the shift truck and needs a photo or a note", async ({ page }) => {
  await setup(page);
  const send = page.getByRole("button", { name: "Send report", exact: true });
  await expect(send).toBeDisabled();
  await page.getByLabel("Note (optional)").fill("Loose mirror");
  await expect(send).toBeEnabled();
  await page.getByLabel("Note (optional)").fill(" ");
  await expect(send).toBeDisabled();
  await page.getByLabel("Take photo (up to 5)").setInputFiles(photoPath);
  await expect(page.getByRole("img", { name: "Defect photo 1" })).toBeVisible();
  await expect(send).toBeEnabled();
});

test("a report sent offline waits to sync with its photo and sends once online", async ({
  page,
  context,
}) => {
  const state = await setup(page);
  let uploads = 0;
  let grants = 0;
  const grant: Schema["UploadLinkResult"] = {
    fileId: photoId,
    url: { url: "https://uploads.example.test/defect", expiresAt: "2026-11-11T10:00:00Z" },
    expiresAt: "2026-11-11T10:00:00Z",
    requiredHeaders: { "Content-Type": "image/jpeg" },
  };
  await page.route("**/api/v1/driver/defects/upload-links", (route) => {
    grants += 1;
    return route.fulfill({ json: grant });
  });
  await page.route(grant.url.url, async (route) => {
    uploads += 1;
    expect(route.request().postDataBuffer()!.length).toBeGreaterThan(0);
    await route.fulfill({ status: 200 });
  });
  await context.setOffline(true);
  await page.getByLabel("Take photo (up to 5)").setInputFiles(photoPath);
  await page.getByRole("button", { name: "Send report", exact: true }).click();
  await expect(page.getByText("Waiting to sync", { exact: true })).toBeVisible();
  expect(state.commands).toHaveLength(0);
  expect(grants).toBe(0);
  await context.setOffline(false);
  await expect
    .poll(() => state.commands.filter((command) => command.type === "defect.report").length)
    .toBe(1);
  expect(state.commands.find((command) => command.type === "defect.report")?.payload).toMatchObject({
    vehicleId: truckId,
    photos: [photoId],
  });
  await expect(page.getByText("Waiting to sync", { exact: true })).toHaveCount(0);
  await context.setOffline(true);
  await context.setOffline(false);
  await page.clock.runFor(60_000);
  expect(state.commands.filter((command) => command.type === "defect.report")).toHaveLength(1);
  expect(uploads).toBe(1);
  expect(grants).toBe(1);
});

test("critical report shows the off road message, a call button and a 60 second cancel", async ({ page }) => {
  await setup(page);
  await critical(page);
  await expect(page.getByText("Vehicle is off road. Call dispatch", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Call dispatch", exact: true })).toHaveAttribute(
    "href",
    "tel:+40123456789",
  );
  await expect(page.getByRole("button", { name: "Cancel report", exact: true })).toBeVisible();
  await expect(page.getByText("60 s", { exact: true })).toBeVisible();
});

test("cancel disappears after 60 seconds", async ({ page }) => {
  await setup(page);
  await critical(page);
  await page.clock.runFor(59_000);
  await expect(page.getByRole("button", { name: "Cancel report", exact: true })).toBeVisible();
  await page.clock.runFor(1_001);
  await expect(page.getByRole("button", { name: "Cancel report", exact: true })).toHaveCount(0);
});

test("too late to cancel shows the server refusal", async ({ page }) => {
  const state = await setup(page);
  state.refuse();
  await critical(page);
  await expect.poll(() => state.commands.length).toBe(1);
  await page.getByRole("button", { name: "Cancel report", exact: true }).click();
  await expect(
    page
      .getByRole("status")
      .filter({ hasText: "Sent. Dispatch will confirm." })
      .getByText("Too late to cancel. Call dispatch.", { exact: true }),
  ).toBeVisible();
  expect(state.commands.map((command) => command.type)).toEqual(["defect.report", "defect.cancel"]);
  expect(state.commands[1].aggregateId).toBe(state.commands[0].aggregateId);
});

test("truck drawer lists open defects without the reporter", async ({ page }) => {
  await setup(page);
  const snapshot: Schema["DriverSnapshot"] = {
    employeeId: "00000000-0000-0000-0000-000000000031",
    generatedAt: "2026-11-10T10:00:00Z",
    canDrive: true,
    readinessCauses: [],
    documents: [],
    cycles: [
      {
        id: shift.id,
        vehicleId: truckId,
        vehiclePlate: "B-TEST 32",
        from: "2026-11-09",
        to: "2026-11-22",
        status: "active",
        source: "generated",
        version: 1,
      },
    ],
  };
  await page.route("**/api/v1/driver/snapshot", (route) => route.fulfill({ json: snapshot }));
  const detail: Schema["DriverVehicleDetail"] = {
    id: truckId,
    plate: "B-TEST 32",
    items: [],
    documents: [],
    defects: [
      {
        id: photoId,
        category: "brakes",
        severity: "critical",
        date: "2026-11-09",
        photos: [{ id: photoId, link: "https://photos.example.test/brakes" }],
      },
    ],
  };
  await page.route("**/api/v1/driver/vehicles/*", (route) => route.fulfill({ json: detail }));
  await page.getByRole("link", { name: "Week", exact: true }).click();
  await page.getByRole("button", { name: "Refresh schedule", exact: true }).click();
  await page.getByRole("button", { name: "Open truck B-TEST 32", exact: true }).click();
  const drawer = page.getByRole("dialog");
  await expect(drawer.getByRole("heading", { name: "Open defects" })).toBeVisible();
  await expect(drawer).toContainText("brakes critical 09.11.2026");
  await expect(drawer.getByRole("button", { name: /photo 1/i })).toBeVisible();
  await expect(drawer).not.toContainText("Dana Driver");
  await expect(drawer).not.toContainText(/reported by/i);
});
