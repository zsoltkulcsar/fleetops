import { useId, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@fleetops/api-client";
import { checked } from "../people/api";
import { useOnline } from "../shell/Shell";
import type { AttentionItem } from "./api";

export function RuleAcknowledgement({ item }: { item: AttentionItem }) {
  const fieldId = useId();
  const online = useOnline();
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (!open)
    return (
      <button className="ui-button" disabled={!online} onClick={() => setOpen(true)}>
        Acknowledge
      </button>
    );
  return (
    <form
      onSubmit={async (event) => {
        event.preventDefault();
        if (!note.trim()) return;
        setBusy(true);
        setError("");
        try {
          await apiClient
            .POST("/api/v1/alerts/{id}/acknowledge-rule-warning", {
              params: { path: { id: item.id } },
              body: { version: item.version, note: note.trim() },
            })
            .then(checked);
          await client.invalidateQueries({ queryKey: ["attention"] });
        } catch {
          setError("Could not acknowledge. Refresh and retry.");
        } finally {
          setBusy(false);
        }
      }}
    >
      <label htmlFor={fieldId}>Acknowledgement note</label>
      <textarea
        id={fieldId}
        required
        maxLength={1000}
        value={note}
        onChange={(event) => setNote(event.target.value)}
      />
      <button className="ui-button" disabled={busy || !online || !note.trim()}>
        Acknowledge
      </button>
      <button className="ui-button" type="button" disabled={busy} onClick={() => setOpen(false)}>
        Cancel
      </button>
      {error && <p role="alert">{error}</p>}
    </form>
  );
}
