import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Banner, EmptyState, StatusPill } from "@fleetops/ui";
import { Search } from "lucide-react";
import { readinessApi } from "../readiness/api";
import { peopleCauseText } from "../readiness/labels";
import { en } from "../i18n/en";
import { useSession } from "../auth/AuthGate";
import { SendEnrolment } from "./SendEnrolment";
import "../fleet/fleet.css";

export function People() {
  const session = useSession();
  const canSendEnrolment =
    session.data?.roles.some((role) => ["hr", "dispatcher", "owner"].includes(role)) === true;
  const [blockedOnly, setBlockedOnly] = useState(false);
  const [search, setSearch] = useState("");
  useEffect(() => {
    document.title = `People · ${en.appName}`;
  }, []);
  const drivers = useQuery({
    queryKey: ["readiness-drivers", blockedOnly],
    queryFn: () => readinessApi.drivers(blockedOnly),
  });
  const visibleDrivers =
    drivers.data?.items.filter((driver) =>
      driver.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()),
    ) ?? [];
  return (
    <div className="page directory-page people-page">
      <div className="page-title">
        <div>
          <h1>People</h1>
        </div>
      </div>
      <div className="directory-tabs" role="group" aria-label="People views">
        <span aria-current="page">People</span>
      </div>
      <section className="ui-panel directory-panel" aria-label="People">
        <header className="directory-toolbar">
          <div className="directory-chips" role="group" aria-label="People filters">
            <button
              type="button"
              className="directory-chip"
              aria-pressed={!blockedOnly}
              onClick={() => setBlockedOnly(false)}
            >
              All
            </button>
            <label className={`directory-chip ${blockedOnly ? "is-selected" : ""}`}>
              <input
                type="checkbox"
                aria-label="Blocked only"
                checked={blockedOnly}
                onChange={(event) => setBlockedOnly(event.target.checked)}
              />
              Blocked only
            </label>
          </div>
          <label className="search-field">
            <Search size={16} aria-hidden />
            <span className="visually-hidden">Search people</span>
            <input placeholder="Name" value={search} onChange={(event) => setSearch(event.target.value)} />
          </label>
        </header>
        {drivers.isError ? (
          <Banner label="Error" retryLabel="Try again" onRetry={() => void drivers.refetch()}>
            People could not be loaded. Please try again.
          </Banner>
        ) : drivers.isLoading ? (
          <div className="fleet-loading" role="status">
            Loading people…
          </div>
        ) : visibleDrivers.length ? (
          <div className="table-wrap">
            <table>
              <caption className="visually-hidden">People driving readiness</caption>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Can drive</th>
                  <th>Documents / restrictions</th>
                  <th>Enrolment</th>
                  {canSendEnrolment && <th>Actions</th>}
                </tr>
              </thead>
              <tbody>
                {visibleDrivers.map((driver) => (
                  <tr key={driver.id}>
                    <td>
                      <strong>{driver.name}</strong>
                    </td>
                    <td>
                      {driver.canDrive ? (
                        <StatusPill status="usable" label="Yes" />
                      ) : (
                        <StatusPill status="blocked" label="No" />
                      )}
                    </td>
                    <td className="directory-reasons">
                      {driver.causes.map((cause) => {
                        const text = peopleCauseText(cause);
                        return text ? <span key={`${cause.code}-${cause.documentType}`}>{text}</span> : null;
                      })}
                    </td>
                    <td>{driver.enrolled ? <span>{en.enrolment.enrolled}</span> : null}</td>
                    {canSendEnrolment && (
                      <td className="directory-actions">
                        <SendEnrolment id={driver.id} name={driver.name} />
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <EmptyState heading={search || blockedOnly ? "No matching people" : "Nothing here yet"}>
            {search || blockedOnly
              ? "Try another name or clear the filters."
              : "There is no work to show in this area yet."}
          </EmptyState>
        )}
        <footer className="directory-footer">
          <span>
            {visibleDrivers.length
              ? `1–${visibleDrivers.length} of ${drivers.data?.items.length ?? 0}`
              : "0 people"}
          </span>
        </footer>
      </section>
    </div>
  );
}
