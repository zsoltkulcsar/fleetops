import { useCallback, useEffect, useState } from "react";
import { scheduleApi } from "./scheduleApi";
import { readSnapshot, replaceSnapshot, type DriverSnapshot } from "./storage/snapshot";

export function useSnapshot() {
  const [snapshot, setSnapshot] = useState<DriverSnapshot>();
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState("");
  const [online, setOnline] = useState(navigator.onLine);
  const refresh = useCallback(async () => {
    if (!navigator.onLine) return;
    setBusy(true);
    setError("");
    try {
      const fresh = await scheduleApi.snapshot();
      await replaceSnapshot(fresh);
      setSnapshot(fresh);
    } catch {
      setError("Your schedule could not be refreshed. Showing the last saved copy, if available.");
    } finally {
      setBusy(false);
    }
  }, []);
  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        const saved = await readSnapshot();
        if (!cancelled) setSnapshot(saved);
      } catch {
        if (!cancelled) setError("Your saved schedule could not be opened. Connect and refresh it.");
      }
      if (!cancelled) setBusy(false);
      if (!cancelled && navigator.onLine) await refresh();
    };
    void load();
    const update = () => {
      setOnline(navigator.onLine);
      if (navigator.onLine) void refresh();
    };
    window.addEventListener("online", update);
    window.addEventListener("offline", update);
    window.addEventListener("fleetops:document-synced", update);
    window.addEventListener("fleetops:defect-synced", update);
    window.addEventListener("fleetops:planning-synced", update);
    window.addEventListener("focus", update);
    const timer = window.setInterval(update, 60_000);
    return () => {
      cancelled = true;
      window.removeEventListener("online", update);
      window.removeEventListener("offline", update);
      window.removeEventListener("fleetops:document-synced", update);
      window.removeEventListener("fleetops:defect-synced", update);
      window.removeEventListener("fleetops:planning-synced", update);
      window.removeEventListener("focus", update);
      window.clearInterval(timer);
    };
  }, [refresh]);
  return { snapshot, busy, error, online, refresh };
}
