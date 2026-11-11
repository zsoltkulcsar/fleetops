import type { Page } from "@playwright/test";
import type { components } from "@fleetops/api-client";
import type { Session } from "../apps/office/src/auth/api";

export const personId = "11111111-1111-1111-1111-111111111111";
export const personRow: components["schemas"]["PersonRow"] = {
  id: personId,
  name: "Dana Driver",
  roles: ["driver"],
  status: "active",
  canDrive: true,
  reasonCategory: null,
  validDocuments: 2,
  requiredDocuments: 2,
  worstState: "verified",
  defaultVehiclePlate: "B-X 1001",
  phone: "+40123456789",
};
export function peoplePage(
  items: components["schemas"]["PersonRow"][] = [personRow],
): components["schemas"]["PeoplePage"] {
  return { items, page: 1, pageSize: 50, total: items.length };
}
export function personDetail(status = "active"): components["schemas"]["PersonDetail"] {
  return {
    header: { ...personRow, status },
    depotId: null,
    fields: {
      firstName: "Dana",
      lastName: "Driver",
      phone: personRow.phone,
      personnelNumber: "D-100",
      email: null,
      startDate: "2026-11-01",
      employmentType: "full_time",
      languagePref: "en",
      adrRequired: false,
    },
    checklist: [
      { type: "licence_c_ce", state: "verified", masked: false, expiresAt: "2027-11-01" },
      { type: "medical", state: "verified", masked: false, expiresAt: "2027-11-02" },
    ],
    verifiedCount: 2,
    requiredCount: 2,
  };
}
export async function officeSession(page: Page, roles = ["hr"]) {
  // The generated session response has no schema; use the application's response type.
  const session: Session = { employeeId: "office-user", roles, stepUpMethod: "password" };
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ json: session }));
}
export async function mockPeople(page: Page, items = [personRow]) {
  const response = peoplePage(items);
  await page.route("**/api/v1/people?*", (route) => route.fulfill({ json: response }));
}
export async function mockPerson(page: Page, detail = personDetail()) {
  await page.route(`**/api/v1/people/${personId}`, (route) => route.fulfill({ json: detail }));
}
export async function fillPerson(page: Page) {
  await page.getByLabel("First name", { exact: true }).fill("Dana");
  await page.getByLabel("Last name", { exact: true }).fill("Driver");
  await page.getByLabel("Phone (international format)", { exact: true }).fill("+40123456789");
  await page.getByLabel("Personnel number", { exact: true }).fill("D-100");
}
// ProblemDetails extensions are omitted by the generator. Keep the base schema and
// attach the API's documented extension values without declaring a parallel shape.
export function peopleProblem(
  code: string,
  extensions: Record<string, string[] | string> = {},
): components["schemas"]["ProblemDetails"] {
  return Object.assign({ status: 409 }, { code }, extensions);
}
export const leavePreview: components["schemas"]["LeavePreview"] = {
  employeeId: personId,
  endDate: "2026-11-11",
  sessionsToRevoke: [personId],
  assignmentsToEnd: [personId],
  assignmentsToRemove: [personId],
  cyclesToEnd: [personId],
  cyclesToRemove: [personId],
  alertsToResolve: [personId],
  runningShiftId: null,
  runningShiftVersion: null,
  shiftsToApprove: [personId],
};
