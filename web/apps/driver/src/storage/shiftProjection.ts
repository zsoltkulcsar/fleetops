import type { components } from "@fleetops/api-client";
import type { QueuedCommand } from "./commandQueueCore";

export type ShiftSnapshot = components["schemas"]["ShiftSnapshot"];
export type ShiftView = components["schemas"]["ShiftView"];
export type ActivityType = components["schemas"]["ActivityType"];

export function resolveSuggestedTruck(
  snapshot: ShiftSnapshot,
  cycles: { vehicleId: string; from: string; to: string; status: string }[],
  date: string,
): string | null {
  const swap = snapshot.assignments
    ?.filter((assignment) => assignment.from <= date && (!assignment.to || assignment.to >= date))
    .filter((assignment) =>
      snapshot.trucks.some((truck) => truck.id === assignment.vehicleId && truck.kind === "truck"),
    )
    .sort((left, right) => right.from.localeCompare(left.from))[0];
  if (swap) return swap.vehicleId;
  const cycle = cycles
    .filter(
      (cycle) =>
        cycle.from <= date &&
        (cycle.to >= date || cycle.status === "overrun") &&
        !["done", "cancelled"].includes(cycle.status),
    )
    .sort((left, right) => right.from.localeCompare(left.from))[0];
  return cycle?.vehicleId ?? null;
}

export function companyDate(instant: string, zone: string): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: zone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date(instant));
}

// Overlay pending commands by aggregate ID; applied IDs in the snapshot never
// appear twice after a lost response or a crash between snapshot and queue writes.
export function projectShifts(snapshot: ShiftSnapshot | undefined, commands: QueuedCommand[]): ShiftView[] {
  const shifts = new Map(structuredClone(snapshot?.shifts ?? []).map((shift) => [shift.id, shift]));
  const applied = new Set(snapshot?.appliedCommandIds ?? []);
  const blocked = new Set<string>();
  for (const command of commands) {
    if (!command.type.startsWith("shift.") || applied.has(command.commandId)) continue;
    if (command.state !== "waiting") {
      const result = command.checkpoint as { shift?: ShiftView } | undefined;
      if (result?.shift) shifts.set(command.aggregateId, structuredClone(result.shift));
      blocked.add(command.aggregateId);
      continue;
    }
    if (blocked.has(command.aggregateId)) continue;
    const payload = command.payload as Record<string, unknown>;
    if (command.type === "shift.start") {
      if (shifts.has(command.aggregateId)) continue;
      shifts.set(command.aggregateId, {
        id: command.aggregateId,
        date: companyDate(command.deviceTime, snapshot?.companyTimeZone ?? "Europe/Berlin"),
        startAt: command.deviceTime,
        endAt: null,
        truckId: payload.workWithoutVehicle
          ? null
          : ((payload.truckId as string | null) ?? snapshot?.suggestedTruckId ?? null),
        trailerId: (payload.trailerId as string | null) ?? null,
        status: "running",
        version: 1,
        rejectReason: null,
        blockedAtStart: null,
        deviceTimeGap: false,
        activities: [
          { id: command.aggregateId, type: "other_work", startAt: command.deviceTime, endAt: null },
        ],
        overnights: [],
      });
      continue;
    }
    const shift = shifts.get(command.aggregateId);
    if (!shift) continue;
    const current = shift.activities.find((activity) => activity.endAt === null);
    switch (command.type) {
      case "shift.switch":
        if (!current || current.type === payload.type) continue;
        current.endAt = command.deviceTime;
        shift.activities.push({
          id: command.commandId,
          type: payload.type as ActivityType,
          startAt: command.deviceTime,
          endAt: null,
        });
        break;
      case "shift.backdate": {
        const last = shift.activities.at(-1);
        const previous = shift.activities.at(-2);
        if (!last || !previous) continue;
        const start = new Date(Date.parse(last.startAt) - Number(payload.minutes) * 60000).toISOString();
        previous.endAt = start;
        last.startAt = start;
        break;
      }
      case "shift.end":
        if (!current) continue;
        current.endAt = (payload.endAt as string | undefined) ?? command.deviceTime;
        shift.endAt = current.endAt;
        shift.status = "draft";
        break;
      case "shift.night": {
        const existing = shift.overnights.find((night) => night.night === payload.night);
        if (existing) {
          existing.countryCode = String(payload.countryCode);
          existing.kind = payload.kind as "cab" | "hotel";
        } else
          shift.overnights.push({
            id: command.commandId,
            night: String(payload.night),
            countryCode: String(payload.countryCode),
            kind: payload.kind as "cab" | "hotel",
          });
        break;
      }
      case "shift.submit":
        shift.status = "submitted";
        shift.rejectReason = null;
        break;
      case "shift.edit-activity": {
        const index = shift.activities.findIndex((activity) => activity.id === payload.activityId);
        if (index < 0) continue;
        const activity = shift.activities[index];
        activity.startAt = String(payload.startAt);
        activity.endAt = payload.endAt as string | null;
        activity.type = payload.type as ActivityType;
        if (index > 0) shift.activities[index - 1].endAt = activity.startAt;
        if (index < shift.activities.length - 1 && activity.endAt)
          shift.activities[index + 1].startAt = activity.endAt;
        break;
      }
      default:
        continue;
    }
    shift.version += 1;
  }
  return [...shifts.values()].sort((left, right) => left.startAt.localeCompare(right.startAt));
}

export function shiftHours(shift: ShiftView, asOf: number = Date.now()): number {
  return shift.activities
    .filter((activity) => activity.type !== "break")
    .reduce(
      (hours, activity) =>
        hours +
        Math.max(0, (activity.endAt ? Date.parse(activity.endAt) : asOf) - Date.parse(activity.startAt)) /
          3600000,
      0,
    );
}
