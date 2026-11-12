import type { components } from "@fleetops/api-client";
import type { Vehicle } from "../fleet/api";
import type { DriverReadinessItem } from "../readiness/api";
import type { ScheduleCycle } from "../schedule/api";
import { addDays, dateLabel } from "./dates";

export type ScheduleSwap = components["schemas"]["ScheduleSwap"];
export type ScheduleMaintenance = components["schemas"]["ScheduleMaintenance"];
export type ScheduleRequest = components["schemas"]["ScheduleRequestView"];
export type IssueKind = "off_road" | "late" | "no_driver" | "clash";
export type PlanIssue = { kind: IssueKind; label: string; cycle?: ScheduleCycle };
export type PlanRow = {
  id: string;
  plate: string;
  vehicle?: Vehicle;
  cycles: ScheduleCycle[];
  swaps: ScheduleSwap[];
  maintenance: ScheduleMaintenance[];
  requests: { request: ScheduleRequest; cycle: ScheduleCycle }[];
  current?: ScheduleCycle;
  next?: ScheduleCycle;
  overrun?: ScheduleCycle;
  swapNow?: ScheduleSwap;
  blockedDriver: boolean;
  issue?: PlanIssue;
  status: { label: string; tone: "crit" | "warn" | "ok" | "idle" };
};
export type Candidate = { id: string; name: string; reason: string };

const covers = (from: string, to: string | null, day: string) => from <= day && (to === null || to >= day);

export function buildRows(input: {
  trucks: Vehicle[];
  cycles: ScheduleCycle[];
  swaps: ScheduleSwap[];
  maintenance: ScheduleMaintenance[];
  requests: ScheduleRequest[];
  drivers: DriverReadinessItem[];
  day: string;
  dayInRange: boolean;
}): PlanRow[] {
  const { day, dayInRange } = input;
  const rows = new Map<string, PlanRow>();
  const row = (id: string, plate: string) => {
    if (!rows.has(id))
      rows.set(id, {
        id,
        plate,
        cycles: [],
        swaps: [],
        maintenance: [],
        requests: [],
        blockedDriver: false,
        status: { label: "As planned", tone: "ok" },
      });
    return rows.get(id)!;
  };
  for (const truck of input.trucks) row(truck.id, truck.plate).vehicle = truck;
  for (const cycle of input.cycles) row(cycle.vehicleId, cycle.vehiclePlate).cycles.push(cycle);
  for (const swap of input.swaps) row(swap.vehicleId, swap.vehiclePlate).swaps.push(swap);
  for (const item of input.maintenance) row(item.vehicleId, item.vehiclePlate).maintenance.push(item);
  const cannotDrive = new Set(input.drivers.filter((driver) => !driver.canDrive).map((driver) => driver.id));
  const swappedToday = new Set(
    input.swaps.filter((swap) => covers(swap.from, swap.to, day)).map((swap) => swap.employeeId),
  );
  for (const entry of rows.values()) {
    entry.cycles.sort((left, right) => left.from.localeCompare(right.from));
    const live = entry.cycles.filter((cycle) => cycle.status !== "cancelled");
    for (const request of input.requests) {
      const cycle = live.find((item) => item.id === request.cycleId);
      if (cycle && request.status === "open") entry.requests.push({ request, cycle });
    }
    if (dayInRange) {
      entry.current = live.find(
        (cycle) => covers(cycle.from, cycle.to, day) && cycle.status !== "done" && cycle.status !== "overrun",
      );
      entry.overrun = live.find((cycle) => cycle.status === "overrun");
      entry.next = live.find((cycle) => cycle.from > day);
      entry.swapNow = entry.swaps.find((swap) => covers(swap.from, swap.to, day));
      entry.blockedDriver = entry.current !== undefined && cannotDrive.has(entry.current.employeeId);
    }
    const unusable = entry.vehicle !== undefined && !entry.vehicle.usable;
    const unusableLabel = entry.vehicle?.shownStatus === "in_maintenance" ? "In workshop" : "Off road";
    const clash = live.find((cycle) => cycle.hasClash);
    if (dayInRange && unusable && entry.current && !swappedToday.has(entry.current.employeeId))
      entry.issue = { kind: "off_road", label: unusableLabel, cycle: entry.current };
    else if (dayInRange && entry.overrun)
      entry.issue = { kind: "late", label: "Handover late", cycle: entry.overrun };
    else if (dayInRange && entry.blockedDriver && !entry.swapNow)
      entry.issue = { kind: "no_driver", label: "No driver", cycle: entry.current };
    else if (dayInRange && !unusable && !entry.current && !entry.swapNow && live.length > 0)
      entry.issue = { kind: "no_driver", label: "No driver" };
    else if (clash) entry.issue = { kind: "clash", label: "Clash", cycle: clash };
    if (entry.issue)
      entry.status = { label: entry.issue.label, tone: entry.issue.kind === "off_road" ? "crit" : "warn" };
    else if (dayInRange && unusable) entry.status = { label: unusableLabel, tone: "idle" };
    else if (live.length === 0 && entry.swaps.length === 0)
      entry.status = { label: "No cycles", tone: "idle" };
    else if (entry.requests.length > 0) entry.status = { label: "Change request", tone: "warn" };
  }
  const rank = (entry: PlanRow) => (entry.issue ? 0 : entry.requests.length > 0 ? 1 : 2);
  return [...rows.values()].sort(
    (left, right) => rank(left) - rank(right) || left.plate.localeCompare(right.plate),
  );
}

// Drivers who can take a truck today, most affected first: their own truck is off road,
// they wait for a late handover, or they have no cycle today.
export function candidates(rows: PlanRow[], drivers: DriverReadinessItem[], day: string): Candidate[] {
  const stranded: Candidate[] = [];
  const waiting: Candidate[] = [];
  const free: Candidate[] = [];
  for (const driver of drivers) {
    if (!driver.canDrive) continue;
    if (
      rows.some((row) =>
        row.swaps.some((swap) => swap.employeeId === driver.id && covers(swap.from, swap.to, day)),
      )
    )
      continue;
    const home = rows.find((row) => row.current?.employeeId === driver.id);
    const base = { id: driver.id, name: driver.name };
    if (home) {
      if (home.vehicle && !home.vehicle.usable)
        stranded.push({ ...base, reason: `Assigned truck ${home.plate} is off road.` });
      else if (home.overrun && home.overrun.employeeId !== driver.id)
        waiting.push({ ...base, reason: `Waiting for ${home.plate}. The handover is late.` });
      continue;
    }
    if (rows.some((row) => row.overrun?.employeeId === driver.id)) continue;
    const upcoming = rows
      .flatMap((row) => row.cycles)
      .filter((cycle) => cycle.employeeId === driver.id && cycle.status !== "cancelled" && cycle.from > day)
      .sort((left, right) => left.from.localeCompare(right.from))[0];
    free.push({
      ...base,
      reason: upcoming
        ? `No cycle today. Free until ${dateLabel(addDays(upcoming.from, -1))}`
        : "No cycle in these dates.",
    });
  }
  return [...stranded, ...waiting, ...free];
}

export function headline(rows: PlanRow[]) {
  const issues = rows.filter((row) => row.issue).length;
  const requests = rows.filter((row) => !row.issue && row.requests.length > 0).length;
  if (issues === 1) return "1 truck needs a decision. The rest run as planned.";
  if (issues > 1) return `${issues} trucks need a decision. The rest run as planned.`;
  if (requests === 1) return "1 change request to answer. Every truck runs as planned.";
  if (requests > 1) return `${requests} change requests to answer. Every truck runs as planned.`;
  return "Every truck runs as planned.";
}
