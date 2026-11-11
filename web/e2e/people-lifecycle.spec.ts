import { test, expect } from "./fixtures";
import type { components } from "@fleetops/api-client";
import {
  personId,
  personRow,
  personDetail,
  officeSession,
  mockPeople,
  mockPerson,
  fillPerson,
  peopleProblem,
  leavePreview,
} from "./people-helpers";

const profileUrl = `/people/${personId}`;
test.beforeEach(async ({ page }) => {
  await officeSession(page);
  await mockPeople(page);
  await mockPerson(page);
});

test("people list shows status, can drive and documents per row", async ({ page }) => {
  await mockPeople(page, [
    personRow,
    {
      ...personRow,
      id: "22222222-2222-2222-2222-222222222222",
      name: "Sam Suspended",
      status: "suspended",
      canDrive: false,
      reasonCategory: "suspended",
      validDocuments: 1,
      worstState: "expired",
    },
  ]);
  await page.goto("/people");
  const dana = page.getByRole("row").filter({ hasText: "Dana Driver" });
  await expect(dana.getByRole("cell", { name: "Active", exact: true })).toBeVisible();
  await expect(dana.getByRole("cell", { name: "Yes", exact: true })).toBeVisible();
  await expect(dana.getByRole("cell", { name: /2 of 2 valid/ })).toBeVisible();
  const sam = page.getByRole("row").filter({ hasText: "Sam Suspended" });
  await expect(sam.getByRole("cell", { name: "Suspended", exact: true })).toBeVisible();
  await expect(sam.getByRole("cell", { name: /No.*Suspended/ })).toBeVisible();
  await expect(sam.getByRole("cell", { name: /1 of 2 valid.*Expired/ })).toBeVisible();
});

test("empty people list explains roles and offers import or add", async ({ page }) => {
  await mockPeople(page, []);
  await page.goto("/people");
  await expect(page.getByRole("heading", { name: "No people yet." })).toBeVisible();
  for (const text of [
    "Driver: uses the phone app.",
    "Dispatcher: plans trucks and approves shifts.",
    "HR: people, documents and payroll.",
    "Owner: everything and settings.",
  ]) {
    await expect(page.getByText(text, { exact: true })).toBeVisible();
  }
  await page.getByRole("button", { name: "Import from Excel", exact: true }).click();
  await expect(page.getByRole("dialog")).toContainText("Use Add person for now.");
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "Add person", exact: true }).first().click();
  await expect(page.getByRole("dialog", { name: "Add person" })).toBeVisible();
});

test("hr adds a driver and sees the enrolment result", async ({ page }) => {
  const result: components["schemas"]["PersonSaveResult"] = {
    id: personId,
    status: "onboarding",
    enrolmentSent: true,
  };
  await page.route("**/api/v1/people", (route) => route.fulfill({ json: result }));
  await page.goto("/people");
  await page.getByRole("button", { name: "Add person", exact: true }).click();
  await fillPerson(page);
  const sent = page.waitForRequest(
    (request) => request.url().endsWith("/api/v1/people") && request.method() === "POST",
  );
  await page.getByRole("button", { name: "Save person", exact: true }).click();
  const body: components["schemas"]["CreatePersonRequest"] = (await sent).postDataJSON();
  expect(body.roles).toEqual(["driver"]);
  expect(body.personnelNumber).toBe("D-100");
  await expect(page.getByText("Saved. Enrolment SMS sent.", { exact: true })).toBeVisible();
});

test("add person shows field errors next to the fields", async ({ page }) => {
  const problem = peopleProblem("validation_failed", { fields: ["phone", "personnelNumber"] });
  await page.route("**/api/v1/people", (route) => route.fulfill({ status: 400, json: problem }));
  await page.goto("/people");
  await page.getByRole("button", { name: "Add person", exact: true }).click();
  await fillPerson(page);
  await page.getByRole("button", { name: "Save person", exact: true }).click();
  for (const label of ["Phone (international format)", "Personnel number"]) {
    const input = page.getByLabel(label, { exact: true });
    await expect(input).toHaveAttribute("aria-invalid", "true");
    const field = input.locator("..");
    await expect(field.getByRole("alert")).toBeVisible();
    await expect(field.getByRole("alert")).toContainText(/phone|personnel number/i);
  }
});

test("dispatcher profile shows medical as visible to HR only", async ({ page }) => {
  await officeSession(page, ["dispatcher"]);
  const detail = personDetail();
  detail.checklist[1] = { type: "medical", state: null, masked: true, expiresAt: null };
  await mockPerson(page, detail);
  await page.goto(profileUrl);
  const row = page.getByRole("row").filter({ hasText: "Medical" });
  await expect(row.getByText("Medical \u2014 visible to HR only", { exact: true })).toBeVisible();
  await expect(row.getByRole("link")).toHaveCount(0);
  await expect(row).not.toContainText("2027");
});

test("onboarding checklist shows n of m verified and activates", async ({ page }) => {
  const detail = personDetail("onboarding");
  await mockPerson(page, detail);
  const result: components["schemas"]["PersonSaveResult"] = {
    id: personId,
    status: "active",
    enrolmentSent: false,
  };
  await page.route(`**/api/v1/people/${personId}/activate`, (route) => {
    detail.header.status = "active";
    return route.fulfill({ json: result });
  });
  await page.goto(profileUrl);
  await expect(page.getByRole("heading", { name: "Onboarding checklist" })).toBeVisible();
  await expect(page.getByText("2 of 2 verified", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Activate", exact: true }).click();
  const sent = page.waitForRequest(`**/api/v1/people/${personId}/activate`);
  await page.getByRole("dialog").getByRole("button", { name: "Activate", exact: true }).click();
  const body: components["schemas"]["ActivatePersonRequest"] = (await sent).postDataJSON();
  expect(body.acceptWarning).toBe(false);
  await expect(page.locator(".people-profile-summary")).toContainText("Active");
  await expect(page.getByText("Change saved.", { exact: true })).toBeVisible();
});

test("activate with blocking documents missing asks to confirm starts blocked", async ({ page }) => {
  const detail = personDetail("onboarding");
  detail.checklist[0].state = "missing";
  detail.verifiedCount = 1;
  await mockPerson(page, detail);
  const requests: components["schemas"]["ActivatePersonRequest"][] = [];
  const result: components["schemas"]["PersonSaveResult"] = {
    id: personId,
    status: "active",
    enrolmentSent: false,
  };
  await page.route(`**/api/v1/people/${personId}/activate`, (route) => {
    const body: components["schemas"]["ActivatePersonRequest"] = route.request().postDataJSON();
    requests.push(body);
    if (!body.acceptWarning)
      return route.fulfill({
        status: 409,
        json: peopleProblem("activation_needs_warning", { types: ["licence_c_ce"] }),
      });
    detail.header = {
      ...detail.header,
      status: "active",
      canDrive: false,
      reasonCategory: "document_missing",
    };
    return route.fulfill({ json: result });
  });
  await page.goto(profileUrl);
  await page.getByRole("button", { name: "Activate", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Activate", exact: true }).click();
  await expect(dialog.getByText("Starts blocked, non-driving.", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("alert")).toContainText("C/CE licence");
  expect(requests).toEqual([{ acceptWarning: false }]);
  await dialog.getByRole("button", { name: "Confirm activation with warning" }).click();
  await expect(page.getByText("Change saved.", { exact: true })).toBeVisible();
  expect(requests).toEqual([{ acceptWarning: false }, { acceptWarning: true }]);
  await expect(page.locator(".people-profile-summary")).toContainText("Document missing");
});

test("activate refused lists what is missing", async ({ page }) => {
  const detail = personDetail("onboarding");
  detail.checklist = [{ type: "driver_card", state: "missing", masked: false, expiresAt: null }];
  detail.verifiedCount = 0;
  detail.requiredCount = 1;
  await mockPerson(page, detail);
  await page.route(`**/api/v1/people/${personId}/activate`, (route) =>
    route.fulfill({ status: 409, json: peopleProblem("activation_incomplete", { types: ["driver_card"] }) }),
  );
  await page.goto(profileUrl);
  await page.getByRole("button", { name: "Activate", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Activate", exact: true }).click();
  await expect(dialog.getByRole("heading", { name: "Missing documents" })).toBeVisible();
  await expect(dialog.getByRole("alert")).toContainText("Driver card");
  await expect(
    dialog.getByText("Activation is incomplete. Verify the missing documents before trying again."),
  ).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Activate", exact: true })).toBeDisabled();
});

for (const action of ["suspend", "rehire"] as const) {
  test(
    action === "suspend" ? "suspend asks for a reason and shows suspended" : "rehire asks for a reason",
    async ({ page }) => {
      const detail = personDetail(action === "suspend" ? "active" : "left");
      await mockPerson(page, detail);
      const requests: components["schemas"]["PersonReasonRequest"][] = [];
      const status = action === "suspend" ? "suspended" : "active";
      const result: components["schemas"]["PersonSaveResult"] = {
        id: personId,
        status,
        enrolmentSent: false,
      };
      await page.route(`**/api/v1/people/${personId}/${action}`, (route) => {
        requests.push(route.request().postDataJSON());
        detail.header.status = status;
        return route.fulfill({ json: result });
      });
      await page.goto(profileUrl);
      const label = action === "suspend" ? "Suspend" : "Rehire";
      await page.getByRole("button", { name: label, exact: true }).click();
      const dialog = page.getByRole("dialog");
      await expect(dialog.getByLabel("Reason", { exact: true })).toBeVisible();
      await dialog.getByRole("button", { name: label, exact: true }).click();
      expect(requests).toHaveLength(0);
      expect(
        await dialog
          .getByLabel("Reason", { exact: true })
          .evaluate((element: HTMLTextAreaElement) => element.validity.valueMissing),
      ).toBe(true);
      await dialog.getByLabel("Reason", { exact: true }).fill("Employment reviewed by HR");
      await dialog.getByRole("button", { name: label, exact: true }).click();
      await expect(page.getByText("Change saved.", { exact: true })).toBeVisible();
      expect(requests).toEqual([{ reason: "Employment reviewed by HR" }]);
      await expect(page.locator(".people-profile-summary")).toContainText(
        action === "suspend" ? "Suspended" : "Active",
      );
    },
  );
}

for (const running of [false, true]) {
  test(
    running
      ? "mark as left with a running shift asks for its end time"
      : "mark as left shows side effects before confirming",
    async ({ page }) => {
      const detail = personDetail();
      await mockPerson(page, detail);
      const preview: components["schemas"]["LeavePreview"] = {
        ...leavePreview,
        runningShiftId: running ? personId : null,
        runningShiftVersion: running ? 1 : null,
      };
      await page.route(`**/api/v1/people/${personId}/leave-preview?*`, (route) =>
        route.fulfill({ json: preview }),
      );
      const requests: components["schemas"]["LeavePersonRequest"][] = [];
      const result: components["schemas"]["PersonSaveResult"] = {
        id: personId,
        status: "left",
        enrolmentSent: false,
      };
      await page.route(`**/api/v1/people/${personId}/leave`, (route) => {
        requests.push(route.request().postDataJSON());
        detail.header.status = "left";
        return route.fulfill({ json: result });
      });
      await page.goto(profileUrl);
      await page.getByRole("button", { name: "Mark as left", exact: true }).click();
      const dialog = page.getByRole("dialog");
      const region = dialog.getByRole("region", { name: "Leave preview" });
      for (const text of [
        "1 sessions will be revoked.",
        "1 assignments will end; 1 will be removed.",
        "1 cycles will end; 1 will be removed.",
        "1 alerts will resolve.",
        "1 shifts remain available for approval.",
      ]) {
        await expect(region.getByText(text, { exact: true })).toBeVisible();
      }
      expect(requests).toHaveLength(0);
      await dialog.getByLabel("Reason", { exact: true }).fill("Employment ended");
      await dialog.getByLabel("Employment end date").fill("2026-11-11");
      if (running) {
        await expect(
          dialog.getByText("A shift is running. Enter its end time and reason before confirming."),
        ).toBeVisible();
        await dialog.getByRole("button", { name: "Mark as left", exact: true }).click();
        expect(requests).toHaveLength(0);
        await dialog.getByLabel("Running shift end time (your local time)").fill("2026-11-11T16:30");
        await dialog.getByLabel("Reason for ending the running shift").fill("Final shift completed");
      }
      await dialog.getByRole("button", { name: "Mark as left", exact: true }).click();
      await expect(page.getByText("Change saved.", { exact: true })).toBeVisible();
      expect(requests).toHaveLength(1);
      expect(requests[0].reason).toBe("Employment ended");
      expect(requests[0].runningShiftReason).toBe(running ? "Final shift completed" : null);
      if (running)
        expect(Date.parse(requests[0].runningShiftEnd!)).toBe(
          await page.evaluate(() => new Date("2026-11-11T16:30").getTime()),
        );
      await expect(page.locator(".people-profile-summary")).toContainText("Left");
    },
  );
}

test("access tab revokes a session and resets the PIN", async ({ page }) => {
  const sessions: components["schemas"]["PersonSessions"] = {
    items: [
      {
        id: personId,
        device: "Dana phone",
        createdAt: "2026-11-01T10:00:00Z",
        lastSeenAt: "2026-11-11T10:00:00Z",
        status: "active",
        revokeReason: null,
        pinResetRequestedAt: null,
      },
    ],
  };
  await page.route(`**/api/v1/people/${personId}/sessions`, (route) => route.fulfill({ json: sessions }));
  const result: components["schemas"]["AccessActionResult"] = { code: "ok" };
  await page.route(`**/api/v1/people/${personId}/sessions/${personId}/revoke`, (route) => {
    const body: components["schemas"]["ReasonRequest"] = route.request().postDataJSON();
    expect(body.reason).toBe("Lost phone");
    sessions.items[0].status = "revoked";
    return route.fulfill({ json: result });
  });
  await page.route(`**/api/v1/people/${personId}/pin-reset`, (route) => route.fulfill({ json: result }));
  await page.goto(profileUrl);
  await page.getByRole("tab", { name: "Access", exact: true }).click();
  await page.getByRole("button", { name: "Revoke", exact: true }).click();
  await page.getByRole("dialog").getByLabel("Reason", { exact: true }).fill("Lost phone");
  await page.getByRole("button", { name: "Revoke session", exact: true }).click();
  await expect(
    page.getByRole("article").filter({ hasText: "Dana phone" }).getByText("Revoked", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Reset PIN", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(
    dialog.getByText("The driver will choose a new PIN on their phone, including from the locked screen."),
  ).toBeVisible();
  const sent = page.waitForRequest(`**/api/v1/people/${personId}/pin-reset`);
  await dialog.getByRole("button", { name: "Reset PIN", exact: true }).click();
  expect((await sent).method()).toBe("POST");
  await expect(dialog).toHaveCount(0);
  await expect(page.getByText("Change saved.", { exact: true })).toBeVisible();
});

test("dispatcher has no change number button", async ({ page }) => {
  await officeSession(page, ["dispatcher"]);
  const sessions: components["schemas"]["PersonSessions"] = { items: [] };
  await page.route(`**/api/v1/people/${personId}/sessions`, (route) => route.fulfill({ json: sessions }));
  await page.goto(profileUrl);
  await page.getByRole("tab", { name: "Access", exact: true }).click();
  await expect(page.getByText("No sessions.", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Reset PIN", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Change number", exact: true })).toHaveCount(0);
});

test("global search finds a person and opens the profile", async ({ page }) => {
  const search: components["schemas"]["DocumentSearchResult"] = { documents: [], vehicles: [], people: [] };
  await page.route("**/api/v1/search?*", (route) => route.fulfill({ json: search }));
  await page.goto("/");
  await page.getByRole("textbox", { name: "Search plate, name or document number" }).fill("Dana");
  const result = page.locator(".search-popover").getByRole("link", { name: /Dana Driver/ });
  await expect(result).toContainText("Driver");
  await expect(result).toContainText("Active");
  await result.click();
  await expect(page).toHaveURL(new RegExp(`${profileUrl}$`));
  await expect(page.getByRole("heading", { name: "Dana Driver", exact: true })).toBeVisible();
});

for (const ruleId of ["A-02", "A-13"]) {
  test(
    ruleId === "A-02" ? "a02 card offers suspend driver" : "a13 card acknowledges after review",
    async ({ page }) => {
      await officeSession(page, ["owner"]);
      const attention: components["schemas"]["AttentionResult"] = {
        groups: [
          {
            subjectType: ruleId === "A-02" ? "person" : "vehicle",
            subjectId: personId,
            subjectName: ruleId === "A-02" ? "Dana Driver" : "B-X 1001",
            worstSeverity: "critical",
            items: [
              {
                id: personId,
                ruleId,
                scopeKey: "medical",
                dueAt: "2026-11-11",
                status: "open",
                severity: "critical",
                snoozable: false,
                version: 3,
              },
            ],
          },
        ],
        totals: { open: 1, critical: 1 },
        companyEmpty: false,
      };
      await page.route("**/api/v1/attention", (route) => route.fulfill({ json: attention }));
      const requests: components["schemas"]["ReviewRequest"][] = [];
      const result: components["schemas"]["ReleaseReviewResult"] = {
        alertId: personId,
        status: "acknowledged",
        version: 4,
      };
      await page.route(`**/api/v1/alerts/${personId}/acknowledge`, (route) => {
        requests.push(route.request().postDataJSON());
        attention.groups = [];
        attention.totals = { open: 0, critical: 0 };
        return route.fulfill({ json: result });
      });
      await page.goto("/");
      if (ruleId === "A-02") {
        await page.getByRole("link", { name: "Suspend driver", exact: true }).click();
        await expect(page.getByRole("dialog", { name: "Suspend: Dana Driver" })).toBeVisible();
        await expect(page.getByRole("dialog").getByLabel("Reason", { exact: true })).toBeVisible();
      } else {
        await expect(page.getByRole("button", { name: "Acknowledge", exact: true })).toHaveCount(0);
        await page.getByRole("button", { name: "Review", exact: true }).click();
        const dialog = page.getByRole("dialog", { name: "Review office override" });
        await expect(
          dialog.getByText(
            "A vehicle document release was used. Review the vehicle before acknowledging this alert.",
          ),
        ).toBeVisible();
        await expect(dialog.getByRole("link", { name: "Open vehicle" })).toHaveAttribute(
          "href",
          `/fleet/${personId}`,
        );
        expect(requests).toHaveLength(0);
        await dialog.getByRole("button", { name: "Acknowledge", exact: true }).click();
        await expect(dialog).toHaveCount(0);
        expect(requests).toEqual([{ version: 3 }]);
        await expect(page.getByRole("heading", { name: "Nothing needs attention right now." })).toBeVisible();
      }
    },
  );
}
