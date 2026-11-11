import { useId, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { Banner, Panel, Plate } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import { defectApi } from "./defectApi";
import { DefectState, defectDate, useDefectAccess } from "./defectPresentation";
import { FleetTabs } from "./FleetTabs";
import { DefectDetail } from "./DefectDetail";
import { LogDefect } from "./LogDefect";
import { useOnline } from "../shell/Shell";
import "./defects.css";

export function Defects({ vehicleId }: { vehicleId?: string }) {
  const id = useId();
  const access = useDefectAccess();
  const online = useOnline();
  const [params, setParams] = useSearchParams();
  const [group, setGroup] = useState("open");
  const [severity, setSeverity] = useState<components["schemas"]["DefectSeverity"] | "">("");
  const [page, setPage] = useState(1);
  const [opened, setOpened] = useState<string>();
  const [logging, setLogging] = useState(false);
  const selected = opened ?? params.get("defectId") ?? undefined;
  const query = useQuery({
    queryKey: ["defects", vehicleId, group, severity, page],
    queryFn: () =>
      defectApi.list({ vehicleId, statusGroup: group, severity: severity || undefined, page, pageSize: 25 }),
    enabled: access.canRead,
  });
  const content = (
    <>
      {!access.canRead ? (
        <p>Ask an owner to grant access in Settings, Users &amp; roles.</p>
      ) : (
        <>
          {!online && (
            <Banner kind="offline" label="Offline">
              Showing saved data. Changes need a connection.
            </Banner>
          )}
          <div className="defect-toolbar">
            <label htmlFor={`${id}-status`}>Status</label>
            <select
              id={`${id}-status`}
              value={group}
              onChange={(event) => {
                setGroup(event.target.value);
                setPage(1);
              }}
            >
              <option value="open">Open</option>
              <option value="in_repair">In repair</option>
              <option value="closed">Closed</option>
            </select>
            <label htmlFor={`${id}-severity`}>Severity</label>
            <select
              id={`${id}-severity`}
              value={severity}
              onChange={(event) => {
                setSeverity(event.target.value as typeof severity);
                setPage(1);
              }}
            >
              <option value="">All severities</option>
              <option value="critical">Critical</option>
              <option value="major">Major</option>
              <option value="minor">Minor</option>
            </select>
            {access.canWrite && (
              <button className="ui-button primary" disabled={!online} onClick={() => setLogging(true)}>
                Log a report
              </button>
            )}
          </div>
          {query.isError && (
            <Banner kind="error" label="Error" retryLabel="Try again" onRetry={() => void query.refetch()}>
              Data could not be loaded. Check the connection and try again.
            </Banner>
          )}
          {query.isPending ? (
            <p role="status">Loading defects...</p>
          ) : (
            query.data && (
              <>
                <p className="defect-summary">
                  Open reports: App {query.data.openByOrigin.app} {"\u00b7"} Logged from chat{" "}
                  {query.data.openByOrigin.officeFromChat}
                </p>
                {query.data.items.length ? (
                  <div className="table-wrap">
                    <table>
                      <thead>
                        <tr>
                          {[
                            "Severity",
                            "Vehicle",
                            "Category",
                            "Reported by / at",
                            "Photos",
                            "Status",
                            "Age",
                            "Origin",
                          ].map((heading) => (
                            <th key={heading} scope="col">
                              {heading}
                            </th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {query.data.items.map((report) => (
                          <tr key={report.id}>
                            <td>
                              <DefectState value={report.severity} />
                            </td>
                            <td>
                              <button className="defect-row-link" onClick={() => setOpened(report.id)}>
                                <Plate>{report.plate}</Plate>
                                <span className="visually-hidden">Open defect {report.category}</span>
                              </button>
                            </td>
                            <td>{report.category.replaceAll("_", " ")}</td>
                            <td>
                              {report.reportedByName}
                              <br />
                              {defectDate(report.reportedAt, true)}
                            </td>
                            <td>{report.photosCount}</td>
                            <td>
                              <DefectState
                                value={report.status}
                                label={report.status === "cancelled" ? "Cancelled by driver" : undefined}
                              />
                            </td>
                            <td>{Math.floor(report.ageDays)} days</td>
                            <td>{report.origin === "app" ? "App" : "Logged from chat"}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  <p className="defect-empty">
                    No defects reported. Drivers report defects from their phone.
                  </p>
                )}
                <div className="defect-toolbar">
                  <button className="ui-button" disabled={page === 1} onClick={() => setPage(page - 1)}>
                    Previous
                  </button>
                  <span>
                    Page {page} of {Math.max(1, Math.ceil(query.data.total / query.data.pageSize))}
                  </span>
                  <button
                    className="ui-button"
                    disabled={page * query.data.pageSize >= query.data.total}
                    onClick={() => setPage(page + 1)}
                  >
                    Next
                  </button>
                </div>
              </>
            )
          )}
          {selected && (
            <DefectDetail
              key={selected}
              id={selected}
              initialAction={params.get("action") ?? undefined}
              onClose={() => {
                setOpened(undefined);
                setParams((previous) => {
                  previous.delete("defectId");
                  previous.delete("action");
                  return previous;
                });
              }}
            />
          )}
          {logging && (
            <LogDefect
              vehicleId={vehicleId}
              onClose={() => setLogging(false)}
              onSaved={(reportId) => {
                setLogging(false);
                setOpened(reportId);
                void query.refetch();
              }}
            />
          )}
        </>
      )}
    </>
  );
  return vehicleId ? (
    <Panel title="Defects">{content}</Panel>
  ) : (
    <div className="page fleet-page">
      <div className="page-title">
        <h1 tabIndex={-1}>Fleet {"\u00b7"} Defects</h1>
      </div>
      <FleetTabs />
      <Panel title="Defect reports">{content}</Panel>
    </div>
  );
}
