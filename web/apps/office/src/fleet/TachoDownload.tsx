import { useId, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@fleetops/api-client";
import { Drawer } from "@fleetops/ui";
import { checked } from "./defectApi";
import { companyToday, defectDate } from "./defectPresentation";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";

export function TachoDownload({
  subjectId,
  subjectType,
  lastDate,
}: {
  subjectId: string;
  subjectType: "vehicle" | "employee";
  lastDate?: string | null;
}) {
  const id = useId();
  const session = useSession();
  const online = useOnline();
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const allowed = session.data?.roles.some((role) => ["owner", "dispatcher"].includes(role));
  return (
    <div>
      <p>Last download: {lastDate ? defectDate(lastDate) : "Not recorded"}</p>
      {allowed && (
        <button className="ui-button" disabled={!online} onClick={() => setOpen(true)}>
          Log download
        </button>
      )}
      {open && (
        <Drawer open title="Log download done" closeLabel="Close download" onClose={() => setOpen(false)}>
          <form
            className="fleet-form"
            onSubmit={async (event) => {
              event.preventDefault();
              const form = new FormData(event.currentTarget);
              setBusy(true);
              setError("");
              try {
                const body = { date: String(form.get("date")) };
                if (subjectType === "vehicle")
                  await apiClient
                    .POST("/api/v1/fleet/vehicles/{id}/tacho-downloads", {
                      params: { path: { id: subjectId } },
                      body,
                    })
                    .then(checked);
                else
                  await apiClient
                    .POST("/api/v1/people/{id}/tacho-downloads", {
                      params: { path: { id: subjectId } },
                      body,
                    })
                    .then(checked);
                await client.invalidateQueries();
                setOpen(false);
              } catch {
                setError("Download could not be logged. Check the date and try again.");
              } finally {
                setBusy(false);
              }
            }}
          >
            <label htmlFor={id}>Download date</label>
            <input
              id={id}
              name="date"
              type="date"
              required
              max={companyToday()}
              defaultValue={companyToday()}
              disabled={busy}
            />
            {error && <p role="alert">{error}</p>}
            <button className="ui-button primary" disabled={busy || !online}>
              Save
            </button>
          </form>
        </Drawer>
      )}
    </div>
  );
}
