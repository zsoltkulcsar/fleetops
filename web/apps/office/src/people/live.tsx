import { StatusPill } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import { absenceLabels } from "../absences/api";
import type { PersonRow } from "./api";
import { addDays, shortDate, type AbsenceItem } from "./directory";
import { PersonStatus } from "./PersonStatus";

type WeekRow = components["schemas"]["WeekRow"];

/**
 * What the office can say about a person right now. Status comes from data FleetOps has
 * (employment status, approved absences, today's shift). Position does not: there is no
 * tachograph or GPS source, so the position line is a sample value marked "Demo" (decisions Round 8).
 */
export type LiveState =
  | { kind: "employment" }
  | { kind: "leave"; type: components["schemas"]["AbsenceType"]; until: string }
  | { kind: "on-shift" }
  | { kind: "shift-ended" }
  | { kind: "off-shift" };

export function currentAbsence(absences: AbsenceItem[] | undefined, today: string) {
  return (absences ?? []).find(
    (item) => item.absence.status === "approved" && item.absence.from <= today && item.absence.to >= today,
  );
}

export function liveState(
  person: PersonRow,
  today: string,
  absences: AbsenceItem[] | undefined,
  week: WeekRow[] | undefined,
): LiveState {
  if (person.status !== "active") return { kind: "employment" };
  const away = currentAbsence(absences, today);
  if (away) return { kind: "leave", type: away.absence.type, until: away.absence.to };
  // Shifts are recorded for drivers only, and only once this week's grid has loaded.
  if (!person.roles.includes("driver") || !week) return { kind: "employment" };
  const day = week.find((row) => row.employeeId === person.id)?.days.find((cell) => cell.date === today);
  if (day?.shifts.some((shift) => shift.status === "running")) return { kind: "on-shift" };
  if (day?.shifts.length) return { kind: "shift-ended" };
  return { kind: "off-shift" };
}

const sampleRoads = [
  "A7 near Kassel",
  "A3 near W\u00fcrzburg",
  "A2 near Hannover",
  "A9 near N\u00fcrnberg",
  "A61 near Koblenz",
  "A1 near Bremen",
];
const sampleEtas = ["14:30", "16:10", "17:45", "19:20"];

function pick(id: string, list: string[]) {
  let hash = 0;
  for (const char of id) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return list[hash % list.length];
}

/** Sample position for the Demo: stable per person, never real. */
export function samplePosition(id: string, onShift: boolean) {
  return onShift ? `${pick(id, sampleRoads)} \u00b7 ETA ${pick(id, sampleEtas)}` : "At the depot";
}

export function DemoTag() {
  return (
    <span className="people-demo-tag" title="Sample value. No tachograph or GPS source is connected.">
      Demo<span className="visually-hidden"> sample value</span>
    </span>
  );
}

export function LiveStatus({ person, state }: { person: PersonRow; state: LiveState }) {
  if (state.kind === "employment") return <PersonStatus status={person.status} />;
  if (state.kind === "leave")
    return (
      <div className="people-live">
        <StatusPill status="absence" label="On leave" />
        <span className="people-live-sub">
          {absenceLabels[state.type]} {"\u00b7"} back {shortDate(addDays(state.until, 1))}
        </span>
      </div>
    );
  const onShift = state.kind === "on-shift";
  return (
    <div className="people-live">
      {onShift ? (
        <StatusPill status="on-shift" label="On shift" />
      ) : (
        <StatusPill status="absent" label={state.kind === "shift-ended" ? "Shift ended" : "Off shift"} />
      )}
      <span className="people-live-sub">
        {samplePosition(person.id, onShift)} <DemoTag />
      </span>
    </div>
  );
}
