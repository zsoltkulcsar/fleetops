import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { EmptyState } from "@fleetops/ui";
import { apiClient, type paths } from "@fleetops/api-client";
import { useSession } from "../auth/AuthGate";
import { peopleApi } from "../people/api";
import { formatBerlinDateTime } from "../shell/date";

type AuditQuery = NonNullable<paths["/api/v1/audit-log"]["get"]["parameters"]["query"]>;

/** O-16 Audit log (Story 12.7): HR and owner. Special fields arrive as "changed" from the server. */
export function AuditLog() {
  const session = useSession();
  const allowed = session.data?.roles.some((role) => role === "hr" || role === "owner") === true;
  const [person, setPerson] = useState("");
  const [entity, setEntity] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);
  const [exportError, setExportError] = useState("");
  useEffect(() => {
    document.title = "Audit log · FleetOps Office";
  }, []);
  const query: AuditQuery = {
    PersonId: person || undefined,
    EntityType: entity || undefined,
    From: from ? `${from}T00:00:00+01:00` : undefined,
    To: to ? `${to}T23:59:59+01:00` : undefined,
    Page: page,
    PageSize: 50,
  };
  const log = useQuery({
    queryKey: ["audit-log", query],
    queryFn: async () => {
      const result = await apiClient.GET("/api/v1/audit-log", { params: { query } });
      if (result.error || !result.data) throw new Error("audit_unavailable");
      return result.data;
    },
    enabled: allowed,
  });
  const people = useQuery({
    queryKey: ["audit-people"],
    queryFn: () => peopleApi.list({ page: 1, pageSize: 200 }),
    enabled: allowed,
  });
  if (!allowed) return <p role="alert">The audit log is available to HR and owners.</p>;
  async function exportCsv() {
    setExportError("");
    const result = await apiClient.GET("/api/v1/audit-log/export", { params: { query }, parseAs: "blob" });
    if (result.error || !result.data) {
      setExportError("Export failed. Try again.");
      return;
    }
    const url = URL.createObjectURL(result.data as Blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = "audit-log.csv";
    link.click();
    URL.revokeObjectURL(url);
  }
  const pages = log.data ? Math.max(1, Math.ceil(log.data.total / log.data.pageSize)) : 1;
  return (
    <div className="page audit-page">
      <div className="page-title">
        <h1>Audit log</h1>
        <button className="ui-button" type="button" onClick={() => void exportCsv()}>
          Export
        </button>
      </div>
      {exportError && <p role="alert">{exportError}</p>}
      <form className="audit-filters" aria-label="Audit filters" onSubmit={(event) => event.preventDefault()}>
        <label htmlFor="audit-person">Person</label>
        <select
          id="audit-person"
          value={person}
          onChange={(event) => {
            setPerson(event.target.value);
            setPage(1);
          }}
        >
          <option value="">Everyone</option>
          {people.data?.items.map((item) => (
            <option key={item.id} value={item.id}>
              {item.name}
            </option>
          ))}
        </select>
        <label htmlFor="audit-entity">Entity</label>
        <select
          id="audit-entity"
          value={entity}
          onChange={(event) => {
            setEntity(event.target.value);
            setPage(1);
          }}
        >
          <option value="">All</option>
          <option value="Employee">Person</option>
          <option value="Shift">Shift</option>
          <option value="Document">Document</option>
          <option value="Vehicle">Vehicle</option>
          <option value="Absence">Absence</option>
          <option value="Setting">Setting</option>
        </select>
        <label htmlFor="audit-from">From</label>
        <input
          id="audit-from"
          type="date"
          value={from}
          onChange={(event) => {
            setFrom(event.target.value);
            setPage(1);
          }}
        />
        <label htmlFor="audit-to">To</label>
        <input
          id="audit-to"
          type="date"
          value={to}
          onChange={(event) => {
            setTo(event.target.value);
            setPage(1);
          }}
        />
      </form>
      {log.isError && <p role="alert">The audit log could not be loaded.</p>}
      {log.data && log.data.items.length === 0 && (
        <EmptyState heading="No changes recorded yet.">
          Changes appear here as people edit records.
        </EmptyState>
      )}
      {log.data && log.data.items.length > 0 && (
        <>
          <table className="audit-table">
            <thead>
              <tr>
                <th scope="col">Time</th>
                <th scope="col">Who</th>
                <th scope="col">Entity</th>
                <th scope="col">Field</th>
                <th scope="col">Old</th>
                <th scope="col">New</th>
                <th scope="col">Reason</th>
              </tr>
            </thead>
            <tbody>
              {log.data.items.map((row) => (
                <tr key={row.id}>
                  <td>{formatBerlinDateTime(row.time)}</td>
                  <td>{row.who}</td>
                  <td>{row.entityType}</td>
                  <td>{row.field}</td>
                  <td>{row.oldValue ?? "—"}</td>
                  <td>{row.newValue ?? "—"}</td>
                  <td>{row.reason || "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <nav className="audit-pager" aria-label="Audit pages">
            <button
              className="ui-button"
              type="button"
              disabled={page <= 1}
              onClick={() => setPage(page - 1)}
            >
              Previous
            </button>
            <span>
              Page {page} of {pages}
            </span>
            <button
              className="ui-button"
              type="button"
              disabled={page >= pages}
              onClick={() => setPage(page + 1)}
            >
              Next
            </button>
          </nav>
        </>
      )}
    </div>
  );
}
