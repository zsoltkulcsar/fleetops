import { Activity } from "lucide-react";
import { addDays, weekNumber, type AbsenceItem } from "./directory";

/** Share of driver working days (Mon-Fri) lost to approved absences, per week. */
export function weekCapacity(
  absences: AbsenceItem[],
  driverIds: Set<string>,
  monday: string,
): { away: number; pending: number } {
  if (!driverIds.size) return { away: 0, pending: 0 };
  const days = [0, 1, 2, 3, 4].map((offset) => addDays(monday, offset));
  let away = 0;
  let pending = 0;
  for (const item of absences) {
    if (!driverIds.has(item.absence.employeeId)) continue;
    const hit = days.filter((day) => day >= item.absence.from && day <= item.absence.to).length;
    if (item.absence.status === "approved") away += hit;
    else if (item.absence.status === "requested") pending += hit;
  }
  const total = driverIds.size * days.length;
  return { away: Math.round((away / total) * 100), pending: Math.round((pending / total) * 100) };
}

/** Guide thresholds for the strip; a company setting in Epic 15. */
export const capacityWarnAt = 20;
export const capacityCriticalAt = 30;

export function CapacityStrip({
  absences,
  driverIds,
  firstMonday,
  weeks = 5,
}: {
  absences: AbsenceItem[];
  driverIds: Set<string>;
  firstMonday: string;
  weeks?: number;
}) {
  return (
    <section className="capacity-strip" aria-label={`Driver capacity, next ${weeks} weeks`}>
      <span className="capacity-strip-title">
        <Activity size={16} aria-hidden />
        Driver capacity, next {weeks} weeks
      </span>
      <ul>
        {Array.from({ length: weeks }, (_, index) => {
          const monday = addDays(firstMonday, index * 7);
          const { away, pending } = weekCapacity(absences, driverIds, monday);
          const tone = away >= capacityCriticalAt ? "crit" : away >= capacityWarnAt ? "warn" : "ok";
          return (
            <li key={monday} className={`capacity-week tone-${tone}`}>
              <span>
                Week {weekNumber(monday)}
                {index === 0 ? " (now)" : ""}
              </span>
              <strong>{away > 0 ? `${away}% away` : `${100 - away}% available`}</strong>
              {pending > 0 && <small>+{pending}% requested</small>}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
