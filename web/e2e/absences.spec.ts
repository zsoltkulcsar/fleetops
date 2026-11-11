import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import { mockPeople, personId } from "./people-helpers";

type Schema = components["schemas"];
const absenceId = "00000000-0000-0000-0000-000000000081";
const balance: Schema["VacationBalance"] = {
  year: 2026,
  opening: 3,
  entitlement: 20,
  used: 0,
  remaining: 23,
  left: 23,
};
const detail: Schema["AbsenceDetail"] = {
  absence: {
    id: absenceId,
    employeeId: personId,
    from: "2026-11-09",
    to: "2026-11-10",
    halfDay: null,
    type: "vacation",
    status: "requested",
    note: null,
    declineReason: null,
    cancelReason: null,
    workingDays: 2,
  },
  employeeName: "Dana Driver",
  workingDays: 2,
  balancesBefore: [balance],
  balancesAfter: [{ ...balance, used: 2, remaining: 21, left: 21 }],
  othersAbsentCount: 0,
  othersAbsentNames: [],
  dayCounting: "Mon-Fri without Company holidays",
};

async function routes(page: Page, role = "hr", item = detail) {
  await page.clock.setFixedTime(new Date("2026-11-06T10:00:00Z"));
  const session: Schema["AuthSessionResponse"] = {
    employeeId: personId,
    roles: [role],
    stepUpMethod: "password",
  };
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ json: session }));
  await mockPeople(page);
  const list: Schema["AbsenceList"] = { items: [item] };
  await page.route("**/api/v1/absences?*", (route) => route.fulfill({ json: list }));
  const holidays: Schema["HolidayList"] = { items: [] };
  await page.route("**/api/v1/holidays?*", (route) => route.fulfill({ json: holidays }));
  await page.goto("/people/absences");
}

async function fillRecord(page: Page) {
  await page.getByRole("button", { name: "Record absence", exact: true }).click();
  const form = page.getByRole("dialog");
  await form.getByLabel("Person", { exact: true }).selectOption(personId);
  await form.getByLabel("From", { exact: true }).fill("2026-11-09");
  await form.getByLabel("To", { exact: true }).fill("2026-11-10");
  return form;
}

test("hr approves a request with the balance in front of them", async ({ page }) => {
  await routes(page);
  let submitted: Schema["AbsenceDecisionRequest"] | undefined;
  await page.route(`**/api/v1/absences/${absenceId}/decide`, (route) => {
    submitted = route.request().postDataJSON();
    return route.fulfill({ json: { ...detail.absence, status: "approved" } satisfies Schema["AbsenceView"] });
  });
  await page.getByRole("button", { name: "Review request" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("Balance before 23 days");
  await expect(dialog).toContainText("after 21 days");
  await expect(dialog).toContainText("2 working days counted");
  await dialog.getByRole("button", { name: "Approve", exact: true }).click();
  await expect.poll(() => submitted).toEqual({ approve: true, reason: null, confirmOverlap: false });
  await expect(dialog).toHaveCount(0);
});

test("over balance request shows the warning", async ({ page }) => {
  await routes(page, "hr", {
    ...detail,
    balancesBefore: [{ ...balance, remaining: 1, left: 1 }],
    balancesAfter: [{ ...balance, remaining: -1, left: -1 }],
  });
  await page.getByRole("button", { name: "Review request" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("Over balance by 1 days");
  await expect(dialog.getByRole("button", { name: "Approve", exact: true })).toBeEnabled();
});

test("dispatcher can record only sick and other", async ({ page }) => {
  await routes(page, "dispatcher");
  const dialog = await fillRecord(page);
  await expect(dialog.getByLabel("Type", { exact: true }).locator("option")).toHaveText(["Sick", "Other"]);
  await expect(dialog.getByLabel("Note (optional)")).toHaveCount(0);
  let submitted: Schema["RecordAbsenceCommand"] | undefined;
  await page.route("**/api/v1/absences", (route) => {
    submitted = route.request().postDataJSON();
    return route.fulfill({
      json: { ...detail.absence, type: "other", status: "approved" } satisfies Schema["AbsenceView"],
    });
  });
  await dialog.getByLabel("Type", { exact: true }).selectOption("other");
  await dialog.getByLabel("Note (optional)").fill("Appointment");
  await dialog.getByRole("button", { name: "Save absence", exact: true }).click();
  await expect.poll(() => submitted?.type).toBe("other");
  expect(submitted?.note).toBe("Appointment");
  await expect(dialog).toHaveCount(0);
});

async function conflictRoute(page: Page, code: string) {
  const requests: Schema["RecordAbsenceCommand"][] = [];
  // ProblemDetails extensions are not represented by the generated client.
  const problem: Schema["ProblemDetails"] = { status: 409, title: "Conflict" };
  const shifts: Pick<Schema["ShiftView"], "id" | "date" | "status">[] = [
    { id: absenceId, date: "2026-11-09", status: code === "running_shift" ? "running" : "draft" },
  ];
  await page.route("**/api/v1/absences", (route) => {
    const body: Schema["RecordAbsenceCommand"] = route.request().postDataJSON();
    requests.push(body);
    return body.confirmOverlap && code !== "running_shift"
      ? route.fulfill({
          json: { ...detail.absence, type: "sick", status: "approved" } satisfies Schema["AbsenceView"],
        })
      : route.fulfill({ status: 409, json: { ...problem, code, shifts } });
  });
  return requests;
}

test("absence over shifts lists them and saves anyway", async ({ page }) => {
  await routes(page);
  const requests = await conflictRoute(page, "absence_overlaps_shifts");
  const dialog = await fillRecord(page);
  await dialog.getByRole("button", { name: "Save absence", exact: true }).click();
  await expect(dialog).toContainText("No shifts will be deleted.");
  await expect(dialog.getByRole("listitem")).toContainText("9.11.2026 Draft");
  expect(requests[0]?.confirmOverlap).toBe(false);
  await dialog.getByRole("button", { name: "Save anyway", exact: true }).click();
  await expect.poll(() => requests.length).toBe(2);
  expect(requests[1]).toEqual({ ...requests[0], confirmOverlap: true });
  await expect(dialog).toHaveCount(0);
});

test("running shift asks to end it first", async ({ page }) => {
  await routes(page);
  const requests = await conflictRoute(page, "running_shift");
  const dialog = await fillRecord(page);
  await dialog.getByRole("button", { name: "Save absence", exact: true }).click();
  await expect(dialog).toContainText("End the shift before recording this absence.");
  await expect(dialog.getByRole("button", { name: "End shift first" })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Save anyway" })).toHaveCount(0);
  expect(requests).toHaveLength(1);
});

test("a19 card accepts a schedule change", async ({ page }) => {
  const request: Schema["ScheduleRequestView"] = {
    id: absenceId,
    employeeId: personId,
    cycleId: "00000000-0000-0000-0000-000000000082",
    proposedFrom: "2026-11-09",
    proposedTo: "2026-11-10",
    note: "Family commitment",
    status: "open",
    declineReason: null,
    decidedBy: null,
    decidedAt: null,
  };
  const session: Schema["AuthSessionResponse"] = {
    employeeId: personId,
    roles: ["dispatcher"],
    stepUpMethod: "password",
  };
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ json: session }));
  const attention: Schema["AttentionResult"] = {
    groups: [
      {
        subjectType: "schedule_request",
        subjectId: request.id,
        subjectName: "Dana Driver",
        worstSeverity: "warning",
        items: [
          {
            id: "00000000-0000-0000-0000-000000000083",
            ruleId: "A-19",
            severity: "warning",
            scopeKey: "open",
            dueAt: null,
            status: "open",
            snoozable: true,
            version: 1,
            actions: ["Accept", "Decline"],
          },
        ],
      },
    ],
    totals: { open: 1, critical: 0 },
    companyEmpty: false,
  };
  let accepted = false;
  await page.route("**/api/v1/attention", (route) =>
    route.fulfill({
      json: accepted ? { ...attention, groups: [], totals: { open: 0, critical: 0 } } : attention,
    }),
  );
  const list: Schema["ScheduleRequestList"] = { items: [request] };
  await page.route("**/api/v1/schedule/requests", (route) => route.fulfill({ json: list }));
  let submitted: Schema["ScheduleRequestDecisionRequest"] | undefined;
  await page.route(`**/api/v1/schedule/requests/${request.id}/decide`, (route) => {
    submitted = route.request().postDataJSON();
    accepted = true;
    return route.fulfill({
      json: { ...request, status: "accepted" } satisfies Schema["ScheduleRequestView"],
    });
  });
  await page.goto("/attention");
  await page.getByRole("button", { name: "Review change request" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("Family commitment");
  await expect(dialog).toContainText("The pattern stays the same.");
  await dialog.getByRole("button", { name: "Accept", exact: true }).click();
  await expect.poll(() => submitted).toEqual({ accept: true, reason: null });
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Review change request" })).toHaveCount(0);
});
