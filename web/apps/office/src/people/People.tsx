import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router";
import { Banner, Drawer, EmptyState, StatusPill } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { peopleApi, type PeopleQuery } from "./api";
import { personRoles, roleLabels, stateLabels, statusLabels } from "./labels";
import { CanDrive, PersonStatus } from "./PersonStatus";
import { PersonForm } from "./PersonForm";
import { SendEnrolment } from "./SendEnrolment";
import "../fleet/fleet.css";
import "./people.css";

export function People() {
  const session = useSession();
  const navigate = useNavigate();
  const canAccess = session.data?.roles.some((role) => ["hr", "dispatcher", "owner"].includes(role)) === true;
  const online = useOnline();
  const canManage = session.data?.roles.some((role) => ["hr", "owner"].includes(role)) === true;
  const [filters, setFilters] = useState<PeopleQuery>({ page: 1, pageSize: 50 });
  const [adding, setAdding] = useState(false);
  const [importing, setImporting] = useState(false);
  const [saved, setSaved] = useState("");
  const people = useQuery({ queryKey: ["people", filters], queryFn: () => peopleApi.list(filters) });
  useEffect(() => {
    document.title = "People \u00b7 FleetOps";
  }, []);
  const filtered = Boolean(
    filters.role || filters.status || filters.search || filters.blockedOnly || filters.expiringWithin30,
  );
  const change = (next: PeopleQuery) => setFilters((current) => ({ ...current, ...next, page: 1 }));
  const addButton = canManage && (
    <button type="button" className="ui-button primary" disabled={!online} onClick={() => setAdding(true)}>
      Add person
    </button>
  );
  return (
    <div className="page directory-page people-page">
      <header className="page-title">
        <h1 tabIndex={-1}>People</h1>
        {addButton}
      </header>
      <div className="directory-tabs" role="group" aria-label="People views">
        <Link to="/people" aria-current="page">
          People
        </Link>
        <Link to="/people/absences">Absences</Link>
      </div>
      {!online && <Banner label="Offline">Cached people are read-only until the connection returns.</Banner>}
      {saved && <Banner label="Saved">{saved}</Banner>}
      <section className="ui-panel directory-panel" aria-label="People">
        <div className="directory-toolbar people-filters">
          <div>
            <label htmlFor="people-role">Role</label>
            <select
              id="people-role"
              value={filters.role ?? ""}
              onChange={(event) => change({ role: event.target.value || undefined })}
            >
              <option value="">All roles</option>
              {Object.entries(roleLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="people-status">Status</label>
            <select
              id="people-status"
              value={filters.status ?? ""}
              onChange={(event) => change({ status: event.target.value || undefined })}
            >
              <option value="">All statuses</option>
              {Object.entries(statusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div className="people-check">
            <input
              id="people-blocked"
              type="checkbox"
              checked={filters.blockedOnly ?? false}
              onChange={(event) => change({ blockedOnly: event.target.checked })}
            />
            <label htmlFor="people-blocked">Blocked only</label>
          </div>
          <div className="people-check">
            <input
              id="people-expiring"
              type="checkbox"
              checked={filters.expiringWithin30 ?? false}
              onChange={(event) => change({ expiringWithin30: event.target.checked })}
            />
            <label htmlFor="people-expiring">{"Expiring \u226430 days"}</label>
          </div>
          <div>
            <label htmlFor="people-search">Search people</label>
            <input
              id="people-search"
              type="search"
              placeholder="Name or personnel number"
              value={filters.search ?? ""}
              onChange={(event) => change({ search: event.target.value })}
            />
          </div>
          {filtered && (
            <button
              className="ui-button ghost"
              type="button"
              onClick={() => setFilters({ page: 1, pageSize: 50 })}
            >
              Clear filters
            </button>
          )}
        </div>
        {people.isError && (
          <Banner
            kind="error"
            label="People could not be loaded"
            retryLabel="Try again"
            onRetry={() => void people.refetch()}
          >
            Check the connection and try again.
          </Banner>
        )}
        {people.isLoading ? (
          <p role="status" className="fleet-loading">
            {"Loading people\u2026"}
          </p>
        ) : people.data?.items.length ? (
          <div className="table-wrap">
            <table>
              <caption className="visually-hidden">People</caption>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Roles</th>
                  <th>Status</th>
                  <th>Can drive</th>
                  <th>Documents</th>
                  <th>Default vehicle</th>
                  <th>Phone</th>
                  {canAccess && <th>Actions</th>}
                </tr>
              </thead>
              <tbody>
                {people.data.items.map((person) => (
                  <tr
                    key={person.id}
                    tabIndex={0}
                    onClick={(event) => {
                      if (!(event.target instanceof Element && event.target.closest("a, button")))
                        navigate(`/people/${person.id}`);
                    }}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" && event.target === event.currentTarget)
                        navigate(`/people/${person.id}`);
                    }}
                  >
                    <td>
                      <Link to={`/people/${person.id}`}>{person.name}</Link>
                    </td>
                    <td>{personRoles(person.roles)}</td>
                    <td>
                      <PersonStatus status={person.status} />
                    </td>
                    <td>
                      <CanDrive person={person} />
                    </td>
                    <td>
                      {person.validDocuments} of {person.requiredDocuments} valid
                      {person.worstState && (
                        <StatusPill
                          status={
                            person.worstState === "verified"
                              ? "usable"
                              : person.worstState === "expiring" || person.worstState === "uploaded"
                                ? "warning"
                                : person.worstState === "missing"
                                  ? "missing"
                                  : "blocked"
                          }
                          label={stateLabels[person.worstState] ?? "Unknown state"}
                        />
                      )}
                    </td>
                    <td>{person.defaultVehiclePlate ?? "Not assigned"}</td>
                    <td>{person.phone ?? "Not recorded"}</td>
                    {canAccess && (
                      <td>
                        {online && person.roles.includes("driver") && person.status !== "left" && (
                          <SendEnrolment id={person.id} name={person.name} />
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          !people.isError && (
            <EmptyState heading={filtered ? "No matching people" : "No people yet."}>
              {filtered ? (
                <p>Try another name or clear the filters.</p>
              ) : (
                <>
                  <p>Driver: uses the phone app.</p>
                  <p>Dispatcher: plans trucks and approves shifts.</p>
                  <p>HR: people, documents and payroll.</p>
                  <p>Owner: everything and settings.</p>
                  <div className="people-actions">
                    {addButton}
                    {canManage && (
                      <button className="ui-button" type="button" onClick={() => setImporting(true)}>
                        Import from Excel
                      </button>
                    )}
                  </div>
                </>
              )}
            </EmptyState>
          )
        )}
        {people.data && (
          <footer className="directory-footer">
            <span>{people.data.total} people</span>
            <div className="people-actions">
              <button
                type="button"
                className="ui-button"
                disabled={people.data.page <= 1}
                onClick={() => setFilters({ ...filters, page: people.data.page - 1 })}
              >
                Previous
              </button>
              <span>Page {people.data.page}</span>
              <button
                type="button"
                className="ui-button"
                disabled={people.data.page * people.data.pageSize >= people.data.total}
                onClick={() => setFilters({ ...filters, page: people.data.page + 1 })}
              >
                Next
              </button>
            </div>
          </footer>
        )}
      </section>
      {adding && (
        <PersonForm
          onClose={() => setAdding(false)}
          onSaved={(result, patternFailed) => {
            setAdding(false);
            setSaved(
              `${result.enrolmentSent ? "Saved. Enrolment SMS sent." : "Saved. Enrolment SMS not sent. Use Resend enrolment on the Access tab."}${patternFailed ? " Cycle pattern was not saved. Open Plan to set it up." : ""}`,
            );
            void people.refetch();
          }}
        />
      )}
      <Drawer
        open={importing}
        title="Import from Excel"
        closeLabel="Close"
        onClose={() => setImporting(false)}
      >
        <p>Import from Excel comes later, in Story 14.1. Use Add person for now.</p>
      </Drawer>
    </div>
  );
}
