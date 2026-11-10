import type { components } from "@fleetops/api-client";
import type { Page } from "@playwright/test";

export type Schema = components["schemas"];
export const shift: Schema["OfficeShiftView"] = {
  id: "00000000-0000-0000-0000-000000000061",
  date: "2026-11-09",
  startAt: "2026-11-09T08:00:00Z",
  endAt: "2026-11-09T16:00:00Z",
  truckId: null,
  trailerId: null,
  status: "submitted",
  source: "app",
  version: 3,
  departureCheckOk: true,
  odometerStartKm: 100,
  odometerEndKm: 200,
  submittedAt: "2026-11-09T17:00:00Z",
  approvedBy: null,
  approvedAt: null,
  approvalNote: null,
  rejectReason: null,
  activities: [
    {
      id: "00000000-0000-0000-0000-000000000062",
      type: "other_work",
      startAt: "2026-11-09T08:00:00Z",
      endAt: "2026-11-09T16:00:00Z",
    },
  ],
  overnights: [
    { id: "00000000-0000-0000-0000-000000000063", night: "2026-11-09", countryCode: "DE", kind: "cab" },
  ],
  drivingHours: 0,
  otherWorkHours: 8,
  breakHours: 0,
  availabilityHours: 0,
};
export const history: Schema["ShiftHistory"] = {
  auditId: "00000000-0000-0000-0000-000000000064",
  entityType: "Shift",
  entityId: shift.id,
  field: "endAt",
  before: "2026-11-09T15:00:00Z",
  after: shift.endAt,
  editor: "Helen Office",
  reason: "Corrected end from work record",
  at: "2026-11-10T08:00:00Z",
};
export function detail(overrides: Partial<Schema["OfficeShiftView"]> = {}): Schema["ShiftDetail"] {
  return {
    employeeId: "00000000-0000-0000-0000-000000000031",
    driver: "Dana Driver",
    truckPlate: "B-TEST 32",
    shift: { ...shift, ...overrides },
    flags: [],
    conflicts: [],
    history: [history],
  };
}
export function week(): Schema["WeekGrid"] {
  const days: Schema["WeekCell"][] = Array.from({ length: 7 }, (_, index) => ({
    date: `2026-11-${String(9 + index).padStart(2, "0")}`,
    workMinutes: index === 0 ? 480 : 0,
    status: index === 0 ? "awaiting" : index === 1 ? "absence" : index === 2 ? "missing" : "empty",
    label: index === 0 ? "Awaiting" : index === 1 ? "Absence" : index === 2 ? "Missing" : "No shift",
    missing: index === 2,
    warnings: index === 0,
    shifts:
      index === 0 ? [{ id: shift.id, version: shift.version, status: "submitted", flagged: false }] : [],
    absences: index === 1 ? ["Vacation"] : [],
  }));
  return {
    week: "2026-11-09",
    companyTimeZone: "Europe/Berlin",
    page: 1,
    pageSize: 50,
    total: 501,
    counts: { all: 501, awaiting: 12, missing: 4, warnings: 2 },
    cutoff: null,
    rows: [
      {
        employeeId: detail().employeeId,
        driver: "Dana Driver",
        personnelNumber: "D-100",
        days,
        workMinutes: 480,
        nights: [{ countryCode: "DE", count: 1 }],
        status: "awaiting",
        warnings: true,
      },
    ],
  };
}
export async function timesheetRoutes(page: Page, grid = week(), current = detail()) {
  await page.clock.setFixedTime(new Date("2026-11-10T10:00:00Z"));
  await page.route("**/api/v1/timesheets/week?*", (route) => route.fulfill({ json: grid }));
  await page.route("**/api/v1/timesheets/shifts/*", (route) => route.fulfill({ json: current }));
  const people: Schema["PeopleLookupResult"] = { items: [{ id: current.employeeId, name: current.driver }] };
  await page.route("**/api/v1/people/lookup?*", (route) => route.fulfill({ json: people }));
}
export async function openShift(page: Page) {
  await page.goto(`/timesheets?shiftId=${shift.id}`);
}
