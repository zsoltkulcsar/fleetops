import type { components } from "@fleetops/api-client";
import { localStore } from "./localStore";
import { decryptRecord, storeEncryptedRecord, type EncryptedRecord } from "./pinVault";

export type DriverSnapshot = components["schemas"]["DriverSnapshot"];
const snapshotKey = "driver-snapshot";

// The allow-list is deliberate: file bodies, URLs, names of other people and
// future server additions never enter the offline record by accident.
export function snapshotFields(snapshot: DriverSnapshot): DriverSnapshot {
  return {
    employeeId: snapshot.employeeId,
    complianceRules: snapshot.complianceRules?.map(
      ({ id, name, guideValue, value, confirmed, valueSchema }) => ({
        id,
        name,
        guideValue,
        value,
        confirmed,
        valueSchema,
      }),
    ),
    generatedAt: snapshot.generatedAt,
    holidays: snapshot.holidays?.map(({ date, name }) => ({ date, name })),
    vacationBalance: snapshot.vacationBalance
      ? {
          year: snapshot.vacationBalance.year,
          opening: snapshot.vacationBalance.opening,
          entitlement: snapshot.vacationBalance.entitlement,
          used: snapshot.vacationBalance.used,
          remaining: snapshot.vacationBalance.remaining,
          left: snapshot.vacationBalance.left,
        }
      : snapshot.vacationBalance,
    absences: snapshot.absences?.map(
      ({
        id,
        employeeId,
        from,
        to,
        halfDay,
        type,
        status,
        note,
        declineReason,
        cancelReason,
        workingDays,
      }) => ({
        id,
        employeeId,
        from,
        to,
        halfDay,
        type,
        status,
        note,
        declineReason,
        cancelReason,
        workingDays,
      }),
    ),
    scheduleRequests: snapshot.scheduleRequests?.map(
      ({
        id,
        employeeId,
        cycleId,
        proposedFrom,
        proposedTo,
        note,
        status,
        declineReason,
        decidedBy,
        decidedAt,
      }) => ({
        id,
        employeeId,
        cycleId,
        proposedFrom,
        proposedTo,
        note,
        status,
        declineReason,
        decidedBy,
        decidedAt,
      }),
    ),
    canDrive: snapshot.canDrive,
    readinessCauses: snapshot.readinessCauses.map(({ code, documentType }) => ({ code, documentType })),
    cycles: snapshot.cycles.map(({ id, vehicleId, vehiclePlate, from, to, status, source, version }) => ({
      id,
      vehicleId,
      vehiclePlate,
      from,
      to,
      status,
      source,
      version,
    })),
    swaps: snapshot.swaps?.map(({ id, vehicleId, vehiclePlate, from, to }) => ({
      id,
      vehicleId,
      vehiclePlate,
      from,
      to,
    })),
    defects: snapshot.defects?.map(({ id, vehicleId, category, severity, status, reportedAt }) => ({
      id,
      vehicleId,
      category,
      severity,
      status,
      reportedAt,
    })),
    documents: snapshot.documents.map(({ id, type, status, expiresAt, rejectReason }) => ({
      id,
      type,
      status,
      expiresAt,
      rejectReason,
    })),
  };
}

export async function replaceSnapshot(snapshot: DriverSnapshot): Promise<void> {
  if (!snapshot.employeeId || !Number.isFinite(Date.parse(snapshot.generatedAt)))
    throw new Error("invalid_snapshot");
  await storeEncryptedRecord(snapshotKey, snapshotFields(snapshot));
}
export async function readSnapshot(): Promise<DriverSnapshot | undefined> {
  const record = await localStore.get<EncryptedRecord>(snapshotKey);
  return record ? snapshotFields(await decryptRecord<DriverSnapshot>(record)) : undefined;
}
