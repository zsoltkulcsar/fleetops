import type { components } from "@fleetops/api-client";

export const absenceLabels: Record<components["schemas"]["AbsenceType"], string> = {
  vacation: "Vacation",
  sick: "Sick",
  training: "Training",
  unpaid: "Unpaid leave",
  time_off_in_lieu: "Time off in lieu",
  other: "Other",
};
export const absenceStatusLabels: Record<components["schemas"]["AbsenceStatus"], string> = {
  requested: "Requested",
  approved: "Approved",
  declined: "Declined",
  cancelled: "Cancelled",
};
export const requestStatusLabels: Record<components["schemas"]["ScheduleRequestStatus"], string> = {
  open: "Requested",
  accepted: "Accepted",
  declined: "Declined",
  superseded: "Replaced by a newer request",
};
export const cycleStatusLabels: Record<components["schemas"]["CycleStatus"], string> = {
  planned: "Planned",
  active: "Active",
  overrun: "Overrun",
  done: "Done",
  cancelled: "Cancelled",
};
