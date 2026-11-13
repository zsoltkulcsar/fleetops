import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { FileText, Fuel, Gauge, MapPin, Truck, Wrench } from "lucide-react";
import { Banner, Drawer, Plate, StatusPill } from "@fleetops/ui";
import { en } from "../i18n/en";
import { ComingLater } from "../shell/ComingLater";
import { formatDocumentDate } from "../shell/date";
import { documentLabel } from "../people/labels";
import { fleetCauseText } from "../readiness/labels";
import { fleetApi, type Vehicle } from "./api";
import { defectApi } from "./defectApi";
import { defectCategoryLabel, defectSeverityLabel } from "./defectLabels";
import type { VehicleReadiness } from "../readiness/api";

export type VehicleTab = "overview" | "documents" | "service" | "costs";
type Row = Vehicle & VehicleReadiness;

const tabs: { value: VehicleTab; label: string; icon: typeof Truck }[] = [
  { value: "overview", label: "Overview", icon: Truck },
  { value: "documents", label: "Documents", icon: FileText },
  { value: "service", label: "Defects & service", icon: Wrench },
  { value: "costs", label: "Costs & tracking", icon: Fuel },
];

const km = (value: number | null | undefined) =>
  value == null ? null : `${new Intl.NumberFormat("de-DE").format(value)} km`;

export function VehicleQuickView({
  vehicle,
  tab,
  onTab,
  onClose,
  driver,
  canReadDefects,
  status,
}: {
  vehicle: Row;
  tab: VehicleTab;
  onTab: (tab: VehicleTab) => void;
  onClose: () => void;
  driver: string | null;
  canReadDefects: boolean;
  status: { pill: "usable" | "off-road"; label: string };
}) {
  const detail = useQuery({
    queryKey: ["fleet", "vehicle", vehicle.id],
    queryFn: () => fleetApi.detail(vehicle.id),
  });
  const defects = useQuery({
    queryKey: ["fleet", "defects", "open", vehicle.id],
    queryFn: () => defectApi.list({ vehicleId: vehicle.id, statusGroup: "open", page: 1, pageSize: 25 }),
    enabled: tab === "service" && canReadDefects,
  });
  const repairs = useQuery({
    queryKey: ["fleet", "repairs", vehicle.id],
    queryFn: () => defectApi.repairs(vehicle.id),
    enabled: tab === "service" && canReadDefects,
  });
  const [focusTab, setFocusTab] = useState(false);
  const data = detail.data;
  const fact = (label: string, value: string | number | null | undefined) => (
    <div>
      <dt>{label}</dt>
      <dd>{value === null || value === undefined || value === "" ? "Not set" : value}</dd>
    </div>
  );
  const causeFor = (type: string) =>
    (vehicle.causes ?? []).find((cause) => cause.documentType === type) ??
    data?.causes.find((cause) => cause.documentType === type);
  const openRepairs = (repairs.data?.items ?? []).filter((item) =>
    ["scheduled", "in_progress"].includes(item.status),
  );
  return (
    <Drawer open title={vehicle.plate} closeLabel="Close" onClose={onClose}>
      <div className="people-quick vehicle-quick">
        <div className="people-quick-head">
          <span className="vehicle-mark" aria-hidden>
            <Truck size={26} />
          </span>
          <div>
            <p className="people-quick-roles">
              {vehicle.make} {vehicle.model} {"\u00b7"} {vehicle.year}
            </p>
            <div className="people-quick-pills">
              <span className="people-role-badge">{en.fleet.kind[vehicle.kind]}</span>
              <StatusPill status={status.pill} label={status.label} />
              <span className="ui-muted">{en.fleet.ownership[vehicle.ownership]}</span>
            </div>
          </div>
        </div>
        <div className="people-quick-contact">
          <Plate>{vehicle.plate}</Plate>
          <span className="ui-muted">
            Driver today: <strong>{driver ?? "Not planned"}</strong>
          </span>
          <span className="people-quick-contact-later">
            <ComingLater compact title="Live position" />
          </span>
        </div>
        <div className="people-quick-tabs" role="tablist" aria-label={`${vehicle.plate} details`}>
          {tabs.map(({ value, label, icon: Icon }) => (
            <button
              key={value}
              type="button"
              role="tab"
              id={`vehicle-tab-${value}`}
              aria-controls="vehicle-tab-panel"
              aria-selected={tab === value}
              tabIndex={tab === value ? 0 : -1}
              ref={(element) => {
                if (element && focusTab && tab === value) {
                  element.focus();
                  setFocusTab(false);
                }
              }}
              onClick={() => onTab(value)}
              onKeyDown={(event) => {
                if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") return;
                const index = tabs.findIndex((item) => item.value === tab);
                const step = event.key === "ArrowRight" ? 1 : tabs.length - 1;
                onTab(tabs[(index + step) % tabs.length].value);
                setFocusTab(true);
              }}
            >
              <Icon size={15} aria-hidden />
              {label}
              {value === "documents" &&
                (vehicle.causes ?? []).some((cause) => cause.code.startsWith("document")) && (
                  <>
                    <span className="people-tab-dot" aria-hidden />
                    <span className="visually-hidden">, needs attention</span>
                  </>
                )}
            </button>
          ))}
        </div>
        <section id="vehicle-tab-panel" role="tabpanel" aria-labelledby={`vehicle-tab-${tab}`}>
          {detail.isError && tab !== "costs" && (
            <Banner
              kind="error"
              label="Details unavailable"
              retryLabel="Try again"
              onRetry={() => void detail.refetch()}
            >
              Data could not be loaded. Check the connection and try again.
            </Banner>
          )}
          {tab === "overview" && (
            <>
              {(vehicle.causes ?? []).length > 0 && (
                <div className="people-request vehicle-causes">
                  <span className="eyebrow">Why it cannot be used</span>
                  {(vehicle.causes ?? []).map((cause) => (
                    <span key={`${cause.code}-${cause.documentType}`}>{fleetCauseText(cause)}</span>
                  ))}
                </div>
              )}
              <div className="people-quick-grid">
                <div className="people-quick-card">
                  <h3 className="eyebrow">Vehicle</h3>
                  {detail.isLoading && <p role="status">{"Loading details\u2026"}</p>}
                  {data && (
                    <dl className="people-facts">
                      {fact("VIN", data.vin)}
                      {fact(
                        "Gross weight",
                        data.grossWeightKg
                          ? `${new Intl.NumberFormat("de-DE").format(data.grossWeightKg)} kg`
                          : null,
                      )}
                      {fact("Licence class", data.requiredLicenceClass)}
                      {data.kind === "trailer" &&
                        fact(
                          "Trailer type",
                          data.trailerType ? en.fleet.trailerType[data.trailerType] : null,
                        )}
                    </dl>
                  )}
                </div>
                <div className="people-quick-card">
                  <h3 className="eyebrow">
                    <Gauge size={14} aria-hidden /> Use and ownership
                  </h3>
                  {data && (
                    <dl className="people-facts">
                      {fact("Odometer", km(data.odometerKm))}
                      {fact(
                        "Service interval",
                        [
                          km(data.serviceIntervalKm),
                          data.serviceIntervalMonths && `${data.serviceIntervalMonths} months`,
                        ]
                          .filter(Boolean)
                          .join(" or ") || null,
                      )}
                      {fact("Ownership", en.fleet.ownership[data.ownership])}
                      {data.ownership !== "owned" &&
                        fact("Lease ends", formatDocumentDate(data.leaseEndDate))}
                      {data.kind === "truck" &&
                        fact("Last tachograph download", formatDocumentDate(data.lastTachoDownloadAt))}
                    </dl>
                  )}
                </div>
              </div>
              <div className="people-quick-card">
                <div className="people-quick-card-head">
                  <h3 className="eyebrow">Driver and trailer</h3>
                  <Link to="/plan">Change in Plan</Link>
                </div>
                <p className="ui-muted">
                  {driver
                    ? `${driver} drives this vehicle today.`
                    : "No driver planned on this vehicle today."}
                </p>
                <ComingLater compact title="Coupled trailer" />
              </div>
            </>
          )}
          {tab === "documents" && (
            <>
              <div className="people-quick-section-head">
                <div>
                  <h3>Papers this vehicle must carry</h3>
                  <p className="people-quick-lead">
                    Problems come from the readiness check; open the full record for files and dates.
                  </p>
                </div>
              </div>
              {detail.isLoading && <p role="status">{"Loading documents\u2026"}</p>}
              <ul className="people-quick-list">
                {data?.requiredDocuments.map((document) => {
                  const cause = causeFor(document.type);
                  const state = cause
                    ? cause.code === "document_expired"
                      ? { pill: "blocked" as const, label: "Expired" }
                      : { pill: "missing" as const, label: "Missing" }
                    : { pill: "usable" as const, label: "No problem" };
                  return (
                    <li key={document.type} className={`people-doc tone-${state.pill}`}>
                      <div>
                        <strong>{documentLabel(document.type)}</strong>
                        <span>
                          {document.needsConfirmation
                            ? en.fleet.confirmationRequired
                            : en.fleet.confirmationNotRequired}
                        </span>
                      </div>
                      <div className="people-doc-actions">
                        <StatusPill status={state.pill} label={state.label} />
                        {cause && (
                          <Link
                            className="ui-button"
                            to={`/documents?upload=vehicle:${vehicle.id}:${document.type}`}
                          >
                            Upload
                          </Link>
                        )}
                      </div>
                    </li>
                  );
                })}
              </ul>
            </>
          )}
          {tab === "service" && (
            <>
              {!canReadDefects ? (
                <p className="ui-muted">Defects and maintenance are visible to dispatchers and owners.</p>
              ) : (
                <>
                  <div className="people-quick-card">
                    <div className="people-quick-card-head">
                      <h3 className="eyebrow">Open defects</h3>
                      <Link to="/fleet/defects">All defects</Link>
                    </div>
                    {defects.isLoading && <p role="status">{"Loading defects\u2026"}</p>}
                    {defects.data && defects.data.items.length === 0 && (
                      <p className="ui-muted">No open defects.</p>
                    )}
                    <ul className="people-quick-list">
                      {defects.data?.items.map((item) => (
                        <li
                          key={item.id}
                          className={`people-doc tone-${item.severity === "critical" ? "blocked" : item.severity === "major" ? "warning" : "idle"}`}
                        >
                          <div>
                            <strong>{defectCategoryLabel(item.category)}</strong>
                            <span>
                              Reported by {item.reportedByName} {"\u00b7"} {Math.round(item.ageDays)}{" "}
                              {Math.round(item.ageDays) === 1 ? "day" : "days"} ago
                            </span>
                          </div>
                          <StatusPill
                            status={
                              item.severity === "critical"
                                ? "blocked"
                                : item.severity === "major"
                                  ? "warning"
                                  : "info"
                            }
                            label={defectSeverityLabel(item.severity)}
                          />
                        </li>
                      ))}
                    </ul>
                  </div>
                  <div className="people-quick-card">
                    <div className="people-quick-card-head">
                      <h3 className="eyebrow">Planned service and repairs</h3>
                      <Link to="/fleet/maintenance">Maintenance</Link>
                    </div>
                    {repairs.isLoading && <p role="status">{"Loading maintenance\u2026"}</p>}
                    {repairs.data && openRepairs.length === 0 && <p className="ui-muted">Nothing planned.</p>}
                    <ul className="people-quick-list">
                      {openRepairs.map((item) => (
                        <li key={item.id} className={`people-doc tone-${item.overdue ? "blocked" : "idle"}`}>
                          <div>
                            <strong>{item.type.replaceAll("_", " ")}</strong>
                            <span>
                              {item.workshop} {"\u00b7"} {formatDocumentDate(item.scheduledFor)}
                              {item.dueKm != null && ` \u00b7 due at ${km(item.dueKm)}`}
                            </span>
                          </div>
                          <StatusPill
                            status={
                              item.overdue
                                ? "blocked"
                                : item.status === "in_progress"
                                  ? "maintenance"
                                  : "info"
                            }
                            label={
                              item.overdue
                                ? "Overdue"
                                : item.status === "in_progress"
                                  ? "In workshop"
                                  : "Planned"
                            }
                          />
                        </li>
                      ))}
                    </ul>
                  </div>
                </>
              )}
            </>
          )}
          {tab === "costs" && (
            <>
              <ComingLater title="Live position and trip">
                <p>
                  <MapPin size={14} aria-hidden /> Where the vehicle is and which trip it is on. Needs a
                  telematics provider.
                </p>
              </ComingLater>
              <ComingLater title="Fuel and consumption">
                <p>
                  <Fuel size={14} aria-hidden /> Litres per 100 km from fuel card and odometer readings.
                </p>
              </ComingLater>
              <ComingLater title="Cost per kilometre">
                <p>Repairs, tolls, fuel and lease costs divided by distance driven.</p>
              </ComingLater>
              <ComingLater title="Toll device">
                <p>Which on-board unit is fitted to this vehicle.</p>
              </ComingLater>
            </>
          )}
        </section>
        <footer className="people-quick-footer">
          <button type="button" className="ui-button" onClick={onClose}>
            Close
          </button>
          <span className="people-quick-footer-actions">
            <Link className="ui-button primary" to={`/fleet/${vehicle.id}`}>
              Open full record
            </Link>
          </span>
        </footer>
      </div>
    </Drawer>
  );
}
