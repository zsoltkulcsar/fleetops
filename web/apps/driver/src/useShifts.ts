import { useCallback, useEffect, useState } from "react";
import { commandQueue, newCommand, type QueuedCommand } from "./storage/commandQueue";
import { readShifts } from "./storage/shifts";
import { projectShifts, type ShiftSnapshot } from "./storage/shiftProjection";
import { synchronizeShifts } from "./shiftSync";

export function useShifts() {
  const [snapshot, setSnapshot] = useState<ShiftSnapshot>();
  const [commands, setCommands] = useState<QueuedCommand[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const refresh = useCallback(async () => {
    try {
      setSnapshot(await readShifts());
      setCommands(await commandQueue.list());
      return true;
    } catch {
      setError("Saved shifts could not be opened. Retry before recording more work.");
      return false;
    } finally {
      setLoading(false);
    }
  }, []);
  const sync = useCallback(async () => {
    try {
      await synchronizeShifts();
      if (await refresh()) setError("");
    } catch {
      setError("Your shifts could not sync. Saved work remains on this device. Retry when connected.");
    }
  }, [refresh]);
  useEffect(() => {
    void refresh().then(sync);
    const update = () => {
      void refresh();
      void sync();
    };
    const snapshotChanged = () => {
      void refresh();
    };
    window.addEventListener("fleetops:queue-changed", update);
    window.addEventListener("fleetops:shifts-changed", snapshotChanged);
    window.addEventListener("online", update);
    window.addEventListener("focus", update);
    const timer = window.setInterval(update, 30_000);
    return () => {
      window.removeEventListener("fleetops:queue-changed", update);
      window.removeEventListener("fleetops:shifts-changed", snapshotChanged);
      window.removeEventListener("online", update);
      window.removeEventListener("focus", update);
      window.clearInterval(timer);
    };
  }, [refresh, sync]);
  const recordMany = useCallback(
    async (actions: { type: string; aggregateId: string; baseVersion: number; payload: unknown }[]) => {
      setSaving(true);
      setError("");
      try {
        await commandQueue.enqueueMany(
          actions.map(({ type, aggregateId, baseVersion, payload }) =>
            newCommand(type, aggregateId, baseVersion, payload),
          ),
        );
        await refresh();
        return true;
      } catch {
        setError("Your action could not be saved. Keep this screen open and retry.");
        return false;
      } finally {
        setSaving(false);
      }
    },
    [refresh],
  );
  const record = useCallback(
    (type: string, aggregateId: string, baseVersion: number, payload: unknown) =>
      recordMany([{ type, aggregateId, baseVersion, payload }]),
    [recordMany],
  );
  return {
    snapshot,
    commands,
    shifts: projectShifts(snapshot, commands),
    error,
    loading,
    saving,
    record,
    recordMany,
    sync,
  };
}
export type DriverShiftState = ReturnType<typeof useShifts>;
