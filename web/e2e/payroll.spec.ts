import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";

type Schema = components["schemas"];
const employeeId = "00000000-0000-0000-0000-000000000031";
const shiftId = "00000000-0000-0000-0000-000000000061";
const exportId = "00000000-0000-0000-0000-000000000071";
const preview: Schema["PayrollPreview"] = {
  month: "2026-11-01",
  fingerprint: [{ shiftId, version: 4 }],
  days: [],
  totals: [
    {
      employeeId,
      name: "Dana Driver",
      drivingHours: 8,
      otherWorkHours: 2,
      availabilityHours: 1,
      nights: { DE: 1 },
      absences: { vacation: 0.5 },
    },
  ],
};
const entry: Schema["PayrollExportView"] = {
  id: exportId,
  periodFrom: "2026-11-01",
  periodTo: "2026-11-30",
  format: "csv_generic",
  kind: "regular",
  status: "generated",
  version: 1,
  createdBy: employeeId,
  createdAt: "2026-11-06T10:00:00Z",
  sentDate: null,
  note: null,
  reason: null,
  correctionOf: null,
  checksum: "abc",
  shifts: preview.fingerprint,
  deltas: [],
};
async function openPayroll(page: Page, exports: Schema["PayrollExportView"][] = []) {
  await page.route("**/api/v1/payroll/readiness?*", (route) =>
    route.fulfill({
      json: {
        month: "2026-11-01",
        people: [
          {
            employeeId,
            name: "Dana Driver",
            personnelNumber: "P-001",
            approved: 3,
            waiting: 2,
            missing: ["2026-11-04"],
            finalMonth: true,
            ready: false,
          },
        ],
      } satisfies Schema["PayrollReadiness"],
    }),
  );
  await page.route("**/api/v1/payroll/preview?*", (route) => route.fulfill({ json: preview }));
  await page.route("**/api/v1/payroll/exports?*", (route) =>
    route.fulfill({ json: { exports } satisfies Schema["PayrollHistory"] }),
  );
  await page.goto("/payroll");
  await page.getByLabel("Month", { exact: true }).fill("2026-11");
  await expect(page.getByRole("table", { name: "Readiness for 2026-11" })).toBeVisible();
}

test("payroll month shows readiness and final month", async ({ page }) => {
  await openPayroll(page);
  const row = page.getByRole("table", { name: "Readiness for 2026-11" }).getByRole("row").nth(1);
  await expect(row.getByRole("cell")).toHaveText(["3", "2", "2026-11-04", "Final month", "Needs review"]);
  await expect(row.getByRole("rowheader")).toContainText("Dana Driver");
});

test("preview shows totals per person", async ({ page }) => {
  await openPayroll(page);
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  const row = page.getByRole("table", { name: "Preview totals per person" }).getByRole("row").nth(1);
  await expect(row.getByRole("rowheader")).toHaveText("Dana Driver");
  await expect(row.getByRole("cell")).toHaveText(["8", "2", "1", "DE: 1", "vacation: 0.5"]);
});

test("generate asks to confirm period and count", async ({ page }) => {
  await openPayroll(page);
  let requests = 0;
  await page.route("**/api/v1/payroll/exports", (route) => {
    requests++;
    return route.fulfill({ status: 403, json: { code: "step_up_required" } });
  });
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  await page.getByRole("button", { name: "Generate", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Confirm payroll export" });
  await expect(dialog).toContainText("Confirm period 2026-11-01 to 2026-11-30 and 1 shifts.");
  expect(requests).toBe(0);
  await dialog.getByRole("button", { name: "Confirm generate" }).click();
  await expect(dialog.getByLabel("Authentication code")).toBeVisible();
  expect(requests).toBe(1);
});

test("changed shift blocks generate with the list", async ({ page }) => {
  await openPayroll(page);
  await page.route("**/api/v1/payroll/exports", (route) =>
    route.fulfill({ status: 409, json: { code: "export_changed", shifts: [{ shiftId, version: 5 }] } }),
  );
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  await page.getByRole("button", { name: "Generate", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Confirm payroll export" });
  await dialog.getByRole("button", { name: "Confirm generate" }).click();
  await expect(dialog).toContainText(`Shift ${shiftId}, current version 5`);
  await expect(dialog.getByRole("button", { name: "Confirm generate" })).toBeDisabled();
  await expect(dialog.getByRole("button", { name: "Preview again" })).toBeVisible();
});

test("discard asks for a reason", async ({ page }) => {
  await openPayroll(page, [entry]);
  const bodies: unknown[] = [];
  await page.route(`**/api/v1/payroll/exports/${exportId}/discard`, (route) => {
    bodies.push(route.request().postDataJSON());
    return route.fulfill({ json: { ...entry, status: "discarded" } });
  });
  await page.getByRole("button", { name: "Discard", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Discard export" });
  await expect(dialog.getByLabel("Reason", { exact: true })).toHaveAttribute("required", "");
  await dialog.getByRole("button", { name: "Discard export", exact: true }).click();
  expect(bodies).toEqual([]);
  await dialog.getByLabel("Reason", { exact: true }).fill("Wrong period");
  await dialog.getByRole("button", { name: "Discard export", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Export discarded. Shifts are approved again.");
  expect(bodies).toEqual([{ reason: "Wrong period" }]);
});
