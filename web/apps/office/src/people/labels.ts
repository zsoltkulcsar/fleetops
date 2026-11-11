import { en } from "../i18n/en";

export const roleLabels: Record<string, string> = {
  driver: "Driver",
  dispatcher: "Dispatcher",
  hr: "HR",
  owner: "Owner",
};
export const statusLabels: Record<string, string> = {
  onboarding: "Onboarding",
  active: "Active",
  suspended: "Suspended",
  left: "Left",
};
export const stateLabels: Record<string, string> = {
  verified: "Valid",
  expiring: "Expiring",
  uploaded: "Awaiting verification",
  rejected: "Rejected",
  expired: "Expired",
  missing: "Missing",
};
export const reasonLabels: Record<string, string> = {
  document: "Document",
  medical: "Medical",
  document_expired: "Document expired",
  document_missing: "Document missing",
  onboarding: "Not active yet",
  suspended: "Suspended",
  left: "Left",
  not_a_driver: "Not a driver",
  licence_c_ce: "Licence C/CE",
  driver_card: "Driver card",
  code95: "Code 95",
};
export function documentLabel(type: string) {
  return en.documents.types[type as keyof typeof en.documents.types] ?? "Document";
}
export function personStatus(status: string) {
  return statusLabels[status] ?? "Unknown status";
}
export function personRoles(roles: string[]) {
  return roles.map((role) => roleLabels[role] ?? "Other role").join(", ");
}
