import type { DriverShiftState } from "./useShifts";
import type { DriverSnapshot } from "./storage/snapshot";
import {
  breakProgress,
  confirmedWording,
  durationLabel,
  guideWording,
  ruleValue,
  shiftActivities,
} from "./ruleGuidance";

export function DrivingGuidance({
  state,
  today,
  now,
  driver,
}: {
  state: DriverShiftState;
  today: string;
  now: number;
  driver?: DriverSnapshot;
}) {
  const rules = state.snapshot?.complianceRules ?? driver?.complianceRules ?? [];
  const breakRule = rules.find((rule) => rule.id === "C-04");
  const dailyRule = rules.find((rule) => rule.id === "C-01");
  const parsedBreak = ruleValue(breakRule);
  const breakValue =
    parsedBreak &&
    ["drivingMinutes", "breakMinutes", "splitFirstMinutes", "splitLastMinutes"].every((field) =>
      Number.isFinite(parsedBreak[field]),
    )
      ? parsedBreak
      : undefined;
  const parsedDaily = ruleValue(dailyRule);
  const dailyValue = parsedDaily && Number.isFinite(parsedDaily.minutes) ? parsedDaily : undefined;
  const shifts = state.shifts.filter((shift) => shift.date === today);
  const activities = shifts.flatMap((shift) => shiftActivities(shift, now));
  const driving = activities
    .filter((activity) => activity.type === "driving")
    .reduce((minutes, activity) => minutes + activity.minutes, 0);
  const running = shifts.find((shift) => shift.status === "running") ?? shifts.at(-1);
  return (
    <section className="driver-card" aria-label="Driving guidance">
      {breakValue && (
        <div>
          <strong>
            Break due in{" "}
            {durationLabel(
              breakValue.drivingMinutes -
                breakProgress(running ? shiftActivities(running, now) : [], breakValue).continuous,
            )}
          </strong>
          <p>{breakRule?.confirmed ? confirmedWording : guideWording}</p>
        </div>
      )}
      {dailyValue && (
        <div>
          <strong>
            Driving today {durationLabel(driving)} of {durationLabel(dailyValue.minutes)}
          </strong>
          <p>{dailyRule?.confirmed ? confirmedWording : guideWording}</p>
        </div>
      )}
      {!breakValue && <p>Break guidance unavailable. Check against tachograph.</p>}
      {!dailyValue && <p>Daily driving guide unavailable. Check against tachograph.</p>}
    </section>
  );
}
