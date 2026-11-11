import { useState } from "react";
import { apiClient } from "@fleetops/api-client";
import { Banner, Drawer } from "@fleetops/ui";
import { Link } from "react-router";
import { checked } from "../people/api";
import { peopleErrorText } from "../people/errors";
import { useOnline } from "../shell/Shell";
import { formatBerlinDateTime } from "../shell/date";
import type { AttentionGroup, AttentionItem } from "./api";

export function ReleaseReview({
  group,
  item,
  onClose,
  onSaved,
}: {
  group: AttentionGroup;
  item: AttentionItem;
  onClose: () => void;
  onSaved: () => void;
}) {
  const online = useOnline();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  async function acknowledge() {
    setPending(true);
    setError("");
    try {
      await apiClient
        .POST("/api/v1/alerts/{id}/acknowledge", {
          params: { path: { id: item.id } },
          body: { version: item.version },
        })
        .then(checked);
      onSaved();
    } catch (failure) {
      setError(peopleErrorText(failure));
    } finally {
      setPending(false);
    }
  }
  return (
    <Drawer
      open
      title="Review office override"
      closeLabel="Close"
      onClose={() => {
        if (!pending) onClose();
      }}
    >
      <div className="people-form">
        <h3>{group.subjectName}</h3>
        <p>A vehicle document release was used. Review the vehicle before acknowledging this alert.</p>
        {item.dueAt && <p>Due {formatBerlinDateTime(item.dueAt)}</p>}
        <Link className="ui-button" to={`/fleet/${group.subjectId}`} onClick={onClose}>
          Open vehicle
        </Link>
        {error && (
          <Banner kind="error" label="Review could not be saved">
            {error}
          </Banner>
        )}
        <button
          type="button"
          className="ui-button primary"
          disabled={pending || !online}
          onClick={() => void acknowledge()}
        >
          {pending ? "Saving\u2026" : "Acknowledge"}
        </button>
      </div>
    </Drawer>
  );
}
