import { PeopleError } from "./api";

const fieldLabels: Record<string, string> = {
  firstName: "First name",
  lastName: "Last name",
  roles: "Roles",
  phone: "Phone",
  personnelNumber: "Personnel number",
  email: "Email",
  startDate: "Start date",
  employmentType: "Employment type",
  languagePref: "Language",
  vacationEntitlementDays: "Vacation entitlement",
  reason: "Reason",
  endDate: "End date",
  runningShiftEnd: "Running shift end",
  runningShiftReason: "Running shift reason",
};
export function peopleErrorText(error: unknown) {
  if (!(error instanceof PeopleError))
    return "The request could not be completed. Check the connection and try again.";
  if (error.fields.length)
    return `Check these fields: ${error.fields.map((field) => fieldLabels[field] ?? "Form field").join(", ")}.`;
  if (error.status === 403) return "You do not have permission for this action. Contact an owner.";
  if (error.status === 404) return "This person could not be found.";
  if (error.code === "last_owner") return "The last owner cannot be removed.";
  if (error.code === "running_shift_needs_end") return "Enter the running shift end time and a reason.";
  return "The change could not be saved. Refresh the profile and try again.";
}
