import type { components } from "@fleetops/api-client";
import type { ShiftView } from "./storage/shiftProjection";

export type RuleInput = { id: string; value: string | Record<string, number> | null; confirmed: boolean };
export type RuleActivity = { type: string; minutes: number };
export const guideWording = "Guide value \u00b7 rule not yet confirmed";
export const confirmedWording = "Possible breach \u2014 check against tachograph";

export function ruleValue(rule: RuleInput | undefined): Record<string, number> | undefined {
  if (!rule?.value) return undefined;
  try {
    const value = typeof rule.value === "string" ? JSON.parse(rule.value) : rule.value;
    if (
      !value ||
      typeof value !== "object" ||
      Object.values(value).some((item) => typeof item !== "number" || !Number.isFinite(item))
    )
      return undefined;
    return value;
  } catch {
    return undefined;
  }
}

export function breakProgress(activities: RuleActivity[], value: Record<string, number>) {
  let continuous = 0;
  let firstSplit = false;
  let exceeded = false;
  for (const activity of activities) {
    if (activity.type === "driving") {
      continuous += activity.minutes;
      exceeded ||= continuous > value.drivingMinutes;
    }
    if (activity.type !== "break") continue;
    if (
      activity.minutes >= value.breakMinutes ||
      (firstSplit && activity.minutes >= value.splitLastMinutes)
    ) {
      continuous = 0;
      firstSplit = false;
    } else if (activity.minutes >= value.splitFirstMinutes) firstSplit = true;
  }
  return { continuous, exceeded };
}

// Accepts the shared fixture's activity shape as well as snapshot-derived durations.
export function calculateRuleWarnings(
  activities: RuleActivity[],
  rules: RuleInput[],
  scope = "shift",
): components["schemas"]["RuleWarning"][] {
  const total = (type: string) =>
    activities
      .filter((activity) => activity.type === type)
      .reduce((minutes, activity) => minutes + activity.minutes, 0);
  const driving = total("driving");
  const work = driving + total("other_work");
  return rules.flatMap((rule) => {
    const value = ruleValue(rule);
    if (!value) return [];
    const exceeds =
      scope === "week"
        ? ["C-02", "C-03"].includes(rule.id)
          ? driving > value.minutes
          : rule.id === "C-08" && work > value.minutes
        : rule.id === "C-01"
          ? driving > value.extendedMinutes
          : rule.id === "C-04"
            ? breakProgress(activities, value).exceeded
            : rule.id === "C-07" &&
              (work > value.longWorkMinutes
                ? total("break") < value.longBreakMinutes
                : work > value.workMinutes && total("break") < value.breakMinutes);
    return exceeds
      ? [
          {
            ruleId: rule.id,
            severity: rule.confirmed ? "warning" : "info",
            message: rule.confirmed ? confirmedWording : guideWording,
          },
        ]
      : [];
  });
}

export function shiftActivities(shift: ShiftView, now: number): RuleActivity[] {
  return [...shift.activities]
    .sort((left, right) => left.startAt.localeCompare(right.startAt))
    .map((activity) => ({
      type: activity.type,
      minutes:
        Math.max(
          0,
          Date.parse(activity.endAt ?? shift.endAt ?? new Date(now).toISOString()) -
            Date.parse(activity.startAt),
        ) / 60000,
    }));
}

export function durationLabel(minutes: number): string {
  const rounded = Math.max(0, Math.ceil(minutes));
  return `${Math.floor(rounded / 60)}:${String(rounded % 60).padStart(2, "0")}`;
}
