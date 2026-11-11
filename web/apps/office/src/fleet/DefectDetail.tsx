import { useId, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Drawer } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import { defectApi } from "./defectApi";
import { DefectState, defectDate, useDefectAccess } from "./defectPresentation";
import { useOnline } from "../shell/Shell";
import { RepairForm } from "./RepairForm";
import { SwapPicker } from "./SwapPicker";

export function DefectDetail({
  id,
  onClose,
  initialAction,
}: {
  id: string;
  onClose: () => void;
  initialAction?: string;
}) {
  const fieldId = useId();
  const client = useQueryClient();
  const online = useOnline();
  const access = useDefectAccess();
  const query = useQuery({
    queryKey: ["defect", id],
    queryFn: () => defectApi.detail(id),
    enabled: access.canRead,
    staleTime: 0,
    gcTime: 0,
  });
  const [action, setAction] = useState(initialAction ?? "");
  const [reason, setReason] = useState("");
  const [severity, setSeverity] = useState<components["schemas"]["DefectSeverity"]>("minor");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const detail = query.data;
  const repairs = useQuery({
    queryKey: ["defect-repair", detail?.report.vehicleId],
    queryFn: () => defectApi.repairs(detail!.report.vehicleId),
    enabled: !!detail?.repairId && access.canRead,
  });
  const change = async (save: () => Promise<unknown>) => {
    setBusy(true);
    setError("");
    try {
      await save();
      setAction("");
      setReason("");
      await client.invalidateQueries();
      await query.refetch();
    } catch {
      setError("The change could not be saved. Refresh and try again.");
    } finally {
      setBusy(false);
    }
  };
  const active = detail && ["open", "acknowledged", "in_repair"].includes(detail.report.status);
  if (action === "repair" && detail && access.canWrite && active && !detail.repairId)
    return (
      <RepairForm
        vehicleId={detail.report.vehicleId}
        defectId={id}
        onClose={() => setAction("")}
        onSaved={() => {
          setAction("");
          void query.refetch();
        }}
      />
    );
  if (
    action === "swap" &&
    detail &&
    access.canWrite &&
    (!detail.repairId || repairs.data?.items.some((repair) => repair.id === detail.repairId))
  )
    return (
      <SwapPicker
        vehicleId={detail.report.vehicleId}
        repair={repairs.data?.items.find((repair) => repair.id === detail.repairId)}
        onClose={() => setAction("")}
      />
    );
  return (
    <Drawer
      open
      title={
        detail ? `${detail.report.plate}: ${detail.report.category.replaceAll("_", " ")}` : "Defect report"
      }
      closeLabel="Close defect"
      onClose={onClose}
    >
      <div className="fleet-form defect-form">
        {action === "swap" && detail?.repairId && repairs.isPending && (
          <p role="status">Loading linked repair...</p>
        )}
        {repairs.isError && (
          <p role="alert">
            Linked repair could not be loaded.{" "}
            <button onClick={() => void repairs.refetch()}>Try again</button>
          </p>
        )}
        {!access.canRead ? (
          <p>Ask an owner to grant access in Settings, Users &amp; roles.</p>
        ) : query.isPending ? (
          <p role="status">Loading defect...</p>
        ) : query.isError ? (
          <p role="alert">
            Data could not be loaded. Check the connection and try again.{" "}
            <button onClick={() => void query.refetch()}>Try again</button>
          </p>
        ) : (
          detail && (
            <>
              <div className="defect-toolbar">
                <DefectState value={detail.report.severity} />
                <DefectState
                  value={detail.report.status}
                  label={detail.report.status === "cancelled" ? "Cancelled by driver" : undefined}
                />
              </div>
              {detail.report.severity === "critical" && active && (
                <p className="defect-critical">Auto off road</p>
              )}
              <p>
                {detail.report.reportedByName}, {defectDate(detail.report.reportedAt, true)}
              </p>
              <p>{detail.report.origin === "app" ? "App" : "Logged from chat"}</p>
              <p>{detail.description || "No description."}</p>
              <div className="defect-gallery">
                {detail.photos.map((photo, index) => (
                  <a key={photo.id} href={photo.link.url} target="_blank" rel="noreferrer">
                    <img src={photo.link.url} alt={`Defect photo ${index + 1}`} />
                  </a>
                ))}
              </div>
              {detail.closeReason && <p>Closed: {detail.closeReason}</p>}
              {detail.repairId && (
                <p>
                  Linked repair:{" "}
                  {repairs.data?.items.find((repair) => repair.id === detail.repairId)?.workshop ??
                    "Loading repair..."}
                </p>
              )}
              {access.canWrite && active && (
                <div className="defect-toolbar">
                  {detail.report.status === "open" && (
                    <button
                      className="ui-button"
                      disabled={busy || !online}
                      onClick={() => void change(() => defectApi.confirm(id))}
                    >
                      Confirm
                    </button>
                  )}
                  {detail.report.severity !== "minor" && (
                    <button
                      className="ui-button"
                      disabled={busy || !online}
                      onClick={() => setAction("downgrade")}
                    >
                      Downgrade
                    </button>
                  )}
                  {!detail.repairId && (
                    <>
                      <button
                        className="ui-button primary"
                        disabled={busy || !online}
                        onClick={() => setAction("repair")}
                      >
                        Create repair
                      </button>
                      <button
                        className="ui-button"
                        disabled={busy || !online}
                        onClick={() => setAction("close")}
                      >
                        Close
                      </button>
                    </>
                  )}
                  <button
                    className="ui-button"
                    disabled={busy || !online || (!!detail.repairId && !repairs.data)}
                    onClick={() => setAction("swap")}
                  >
                    Swap driver
                  </button>
                </div>
              )}
              {access.canWrite && ["close", "downgrade"].includes(action) && (
                <form
                  className="defect-form"
                  onSubmit={(event) => {
                    event.preventDefault();
                    if (reason.trim())
                      void change(() =>
                        action === "close"
                          ? defectApi.close(id, { reason })
                          : defectApi.downgrade(id, { severity, reason }),
                      );
                  }}
                >
                  {action === "downgrade" && (
                    <>
                      <label htmlFor={`${fieldId}-severity`}>New severity</label>
                      <select
                        id={`${fieldId}-severity`}
                        value={severity}
                        onChange={(event) =>
                          setSeverity(event.target.value as components["schemas"]["DefectSeverity"])
                        }
                        disabled={busy || !online}
                      >
                        <option value="minor">Minor</option>
                        {detail.report.severity === "critical" && <option value="major">Major</option>}
                      </select>
                    </>
                  )}
                  <label htmlFor={`${fieldId}-reason`}>Reason</label>
                  <textarea
                    id={`${fieldId}-reason`}
                    required
                    maxLength={2000}
                    value={reason}
                    onChange={(event) => setReason(event.target.value)}
                    disabled={busy || !online}
                  />
                  <div className="defect-toolbar">
                    <button className="ui-button primary" disabled={busy || !online || !reason.trim()}>
                      Save
                    </button>
                    <button type="button" className="ui-button" disabled={busy} onClick={() => setAction("")}>
                      Cancel
                    </button>
                  </div>
                </form>
              )}
              <h3>History</h3>
              <ul className="history-list">
                {detail.history.map((item, index) => (
                  <li key={index}>
                    {defectDate(item.at, true)}: {item.fieldName} {item.oldValue} {"\u2192"} {item.newValue}.{" "}
                    {item.reason}
                  </li>
                ))}
              </ul>
            </>
          )
        )}
        {error && <p role="alert">{error}</p>}
      </div>
    </Drawer>
  );
}
