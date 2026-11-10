import { scheduleApi } from "./scheduleApi";
import { commandQueue } from "./storage/commandQueue";
import { replaceShifts } from "./storage/shifts";
import { replayShifts } from "./storage/shiftReplay";

export async function synchronizeShifts(): Promise<void> {
  if (!navigator.onLine) return;
  await navigator.locks.request("fleetops-driver-shift-sync", { ifAvailable: true }, async (lock) => {
    if (!lock) return;
    await replayShifts({
      list: commandQueue.list,
      send: scheduleApi.commands,
      checkpoint: commandQueue.checkpoint,
      mark: commandQueue.mark,
      snapshot: scheduleApi.shifts,
      replace: replaceShifts,
      acknowledge: commandQueue.acknowledge,
    });
  });
}
