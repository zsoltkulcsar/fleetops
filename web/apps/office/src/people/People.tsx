import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { EmptyState, Panel, StatusPill } from "@fleetops/ui";
import { readinessApi } from "../readiness/api";
import { en } from "../i18n/en";

export function People() {
  const [blockedOnly, setBlockedOnly] = useState(false);
  useEffect(() => {
    document.title = `People · ${en.appName}`;
  }, []);
  const drivers = useQuery({
    queryKey: ["readiness-drivers", blockedOnly],
    queryFn: () => readinessApi.drivers(blockedOnly),
  });
  return (
    <div className="page">
      <div className="page-title">
        <div>
          <span className="eyebrow">Operations / People</span>
          <h1>People</h1>
          <p className="ui-muted">Keep your team and their work context up to date.</p>
        </div>
      </div>
      <label className="checkbox-field">
        <input
          type="checkbox"
          aria-label="Blocked only"
          checked={blockedOnly}
          onChange={(event) => setBlockedOnly(event.target.checked)}
        />
        Blocked only
      </label>
      {drivers.data?.items.length ? (
        <Panel title="Drivers">
          {drivers.data.items.map((driver) => (
            <div key={driver.id}>
              <strong>{driver.name}</strong>
              {driver.canDrive ? (
                <StatusPill status="usable" />
              ) : (
                <>
                  <StatusPill status="blocked" />
                  <span> Cannot drive</span>
                </>
              )}
            </div>
          ))}
        </Panel>
      ) : (
        <EmptyState heading="Nothing here yet">There is no work to show in this area yet.</EmptyState>
      )}
    </div>
  );
}
