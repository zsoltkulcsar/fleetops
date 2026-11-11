import { useEffect, useState } from "react";
import { commandQueue, type QueuedCommand } from "./storage/commandQueue";

export function usePlanningQueue() {
  const [commands, setCommands] = useState<QueuedCommand[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    const update = () => {
      void commandQueue
        .list()
        .then(setCommands)
        .catch(() => setError("Saved requests could not be opened."));
    };
    const sync = () => {
      update();
    };
    sync();
    window.addEventListener("fleetops:queue-changed", sync);
    window.addEventListener("online", sync);
    window.addEventListener("focus", sync);
    const timer = window.setInterval(sync, 30_000);
    return () => {
      window.removeEventListener("fleetops:queue-changed", sync);
      window.removeEventListener("online", sync);
      window.removeEventListener("focus", sync);
      window.clearInterval(timer);
    };
  }, []);
  return { commands, error };
}
