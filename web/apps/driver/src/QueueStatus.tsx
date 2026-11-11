import { useCallback, useEffect, useState } from "react";
import { commandQueue, type QueuedCommand } from "./storage/commandQueue";
import { readVault } from "./storage/pinVault";
import { flushDefects } from "./defectSync";
import { flushDocuments } from "./documentSync";

export function QueueStatus({ online }: { online: boolean }) {
  const [commands, setCommands] = useState<QueuedCommand[]>([]);
  const [warning, setWarning] = useState("");
  const [error, setError] = useState("");
  const [syncError, setSyncError] = useState("");
  const sync = useCallback(async () => {
    try {
      await flushDocuments();
      await flushDefects();
      setSyncError("");
    } catch {
      setSyncError(
        "Your waiting items could not be sent. They are saved on this device. Retry when connected.",
      );
    }
  }, []);
  useEffect(() => {
    const update = () => {
      void sync();
    };
    update();
    window.addEventListener("online", update);
    window.addEventListener("focus", update);
    window.addEventListener("fleetops:queue-changed", update);
    const timer = window.setInterval(update, 30_000);
    return () => {
      window.removeEventListener("online", update);
      window.removeEventListener("focus", update);
      window.removeEventListener("fleetops:queue-changed", update);
      window.clearInterval(timer);
    };
  }, [sync]);
  const refresh = useCallback(async () => {
    try {
      setCommands(await commandQueue.list());
      const vault = await readVault();
      const estimate = await navigator.storage?.estimate?.();
      setWarning(
        estimate?.quota !== undefined &&
          estimate.usage !== undefined &&
          estimate.quota - estimate.usage < 7_000_000
          ? "Storage is low. Free space before adding photos. Your waiting items are kept."
          : vault && Date.now() - vault.lastOnlineAt >= 7 * 86_400_000
            ? "You have been offline for 7 days. Connect soon to sync your work."
            : "",
      );
      setError("");
    } catch {
      setError("Your waiting items could not be opened. Retry before adding more work.");
    }
  }, []);
  useEffect(() => {
    void refresh();
    const update = () => {
      void refresh();
    };
    window.addEventListener("fleetops:queue-changed", update);
    window.addEventListener("focus", update);
    const timer = window.setInterval(update, 60_000);
    return () => {
      window.removeEventListener("fleetops:queue-changed", update);
      window.removeEventListener("focus", update);
      window.clearInterval(timer);
    };
  }, [refresh, online]);
  if (!warning && !error && !syncError && commands.length === 0) return null;
  return (
    <aside className="driver-sync-status" aria-label="Sync status">
      {warning && (
        <p className="driver-callout" role="status">
          {warning}
        </p>
      )}
      {error && (
        <div className="driver-error" role="alert">
          <p>{error}</p>
          <button onClick={() => void refresh()}>Retry</button>
        </div>
      )}
      {syncError && (
        <div className="driver-error" role="alert">
          <p>{syncError}</p>
          <button disabled={!online} onClick={() => void sync()}>
            Retry sync
          </button>
        </div>
      )}
      {commands.length > 0 && (
        <>
          <p className="driver-sync" role="status">
            {commands.length} item{commands.length === 1 ? "" : "s"} saved on this device
          </p>
          <ul>
            {commands.map((command) => (
              <li key={command.commandId}>
                {command.type === "document.upload" ? "Document photo" : command.type.replaceAll(".", " ")} ·{" "}
                {command.state === "waiting"
                  ? "Waiting to sync"
                  : command.state === "conflict"
                    ? "Office changed this item — review needed"
                    : "Could not sync — retry needed"}
                {command.type === "defect.cancel" &&
                  ["cancel_window_passed", "not_cancellable"].includes(command.errorCode ?? "") && (
                    <p>Too late to cancel. Call dispatch.</p>
                  )}
                {command.errorCode === "running_shift_exists" && (
                  <p>You have an open shift on another device.</p>
                )}
                {command.errorCode === "dependency" && <p>An earlier action needs Office review first.</p>}
                {command.errorCode === "driver_blocked" && (
                  <p>Only work without a vehicle is available. Contact HR.</p>
                )}
                {command.errorCode === "employee_not_active" && (
                  <p>Your account cannot start shifts. Contact HR.</p>
                )}
                {command.state === "failed" && command.type === "document.upload" && (
                  <button
                    disabled={!online}
                    onClick={() =>
                      void commandQueue
                        .retry(command.commandId)
                        .catch(() => setError("Retry could not be saved. Try again."))
                    }
                  >
                    Retry
                  </button>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </aside>
  );
}
