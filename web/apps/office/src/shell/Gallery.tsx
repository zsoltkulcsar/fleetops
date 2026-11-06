import { useEffect, useState } from "react";
import { en } from "../i18n/en";
import {
  AlertCard,
  Banner,
  Drawer,
  EmptyState,
  KpiTile,
  Panel,
  Plate,
  ProgressMeter,
  SegmentedControl,
  Skeleton,
  StatusPill,
  Toast,
  Toggle,
  WeekGridCell,
  statusVocabulary,
  type StatusName,
} from "@fleetops/ui";
import { formatTodayBerlin } from "./date";

export function Gallery() {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [toastOpen, setToastOpen] = useState(false);
  const [retryCount, setRetryCount] = useState(0);
  const [emptyActions, setEmptyActions] = useState(0);
  const [renderTick, setRenderTick] = useState(0);
  const [online, setOnline] = useState(() => navigator.onLine);
  const [checked, setChecked] = useState(false);
  const [segment, setSegment] = useState("all");
  useEffect(() => {
    const on = () => setOnline(true);
    const off = () => setOnline(false);
    window.addEventListener("online", on);
    window.addEventListener("offline", off);
    return () => {
      window.removeEventListener("online", on);
      window.removeEventListener("offline", off);
    };
  }, []);
  return (
    <div className="page gallery">
      <h1>Component and state gallery</h1>
      <p className="ui-muted">Development-only accessibility harness.</p>
      <ThrowingSection />
      <Panel title="Panel states">
        <div className="gallery-grid">
          <section>
            <h2>Empty</h2>
            <EmptyState
              heading="No records yet"
              primary={{ label: "Set up gallery", onClick: () => setEmptyActions((count) => count + 1) }}
            >
              Add your first record to get started.
            </EmptyState>
            <span className="visually-hidden">Empty actions: {emptyActions}</span>
          </section>
          <section>
            <h2>Loading</h2>
            <Skeleton />
            <Skeleton width="80%" />
            <Skeleton width="60%" />
          </section>
          <section>
            <h2>Error</h2>
            <Banner
              kind="error"
              label={en.banner.error}
              retryLabel={en.actions.tryAgain}
              onRetry={() => setRetryCount((count) => count + 1)}
            >
              {en.states.error}
            </Banner>
            <span className="visually-hidden">Retry attempts: {retryCount}</span>
          </section>
          <section>
            <h2>Offline</h2>
            <Banner kind="offline" label={en.banner.offline}>
              {en.states.offline}
            </Banner>
            <button type="button" className="ui-button" disabled={!online}>
              Add item
            </button>
          </section>
          <section>
            <h2>Permission denied</h2>
            <Banner kind="info" label={en.banner.info}>
              {en.states.permissionDenied}
            </Banner>
          </section>
        </div>
      </Panel>
      <Panel title="Components">
        <div className="gallery-grid">
          <section>
            <h2>Alert</h2>
            <AlertCard
              items={[{ subject: "Review item", body: "Action required." }]}
              actionLabel="Review"
              onAction={() => undefined}
            />
          </section>
          <section>
            <h2>KPI</h2>
            <KpiTile label="Open" value="0" onClick={() => undefined} />
          </section>
          <section>
            <h2>Plate</h2>
            <Plate>KS-DC 1042</Plate>
          </section>
          <section>
            <h2>Progress</h2>
            <ProgressMeter label="Completion" value={70} />
          </section>
          <section>
            <h2>Segments</h2>
            <SegmentedControl
              ariaLabel="View options"
              options={[
                { id: "all", label: "All" },
                { id: "mine", label: "Mine" },
              ]}
              value={segment}
              onChange={setSegment}
            />
          </section>
          <section>
            <h2>Toggle</h2>
            <Toggle label="Compact mode" checked={checked} onChange={setChecked} />
          </section>
          <section>
            <h2>Week cell</h2>
            <WeekGridCell date={formatTodayBerlin()} status="valid">
              8 h
            </WeekGridCell>
          </section>
        </div>
      </Panel>
      <Panel title="Status vocabulary">
        <div className="status-gallery" data-testid="status-vocabulary">
          {Object.entries(statusVocabulary).map(([status, definition]) =>
            status === "absence" ? (
              <StatusPill key={status} status="absence" label={definition.label} />
            ) : status === "expiring" || status === "expired" ? (
              <StatusPill key={status} status={status} date="2026-11-05" />
            ) : (
              <StatusPill
                key={status}
                status={status as Exclude<StatusName, "absence" | "expiring" | "expired">}
              />
            ),
          )}
        </div>
      </Panel>
      <div className="gallery-actions">
        <button type="button" className="ui-button primary" onClick={() => setDrawerOpen(true)}>
          Open drawer
        </button>
        <button type="button" className="ui-button" onClick={() => setToastOpen(true)}>
          Show success toast
        </button>
      </div>
      <Drawer
        open={drawerOpen}
        title="Gallery drawer"
        closeLabel={en.actions.close}
        onClose={() => setDrawerOpen(false)}
      >
        <p>Focus should remain in this drawer until it closes.</p>
        <button type="button" className="ui-button" onClick={() => setRenderTick((tick) => tick + 1)}>
          Re-render gallery ({renderTick})
        </button>
        <button type="button" className="ui-button" onClick={() => setDrawerOpen(false)}>
          Done
        </button>
      </Drawer>
      <Toast
        open={toastOpen}
        onClose={() => setToastOpen(false)}
        undo={() => setToastOpen(false)}
        dismissLabel={en.actions.dismiss}
        undoLabel={en.actions.undo}
      >
        Reversible action completed
      </Toast>
    </div>
  );
}

function ThrowingSection() {
  const [failed, setFailed] = useState(false);
  if (failed) throw new Error("Gallery render failure");
  return (
    <section>
      <button type="button" className="ui-button" onClick={() => setFailed(true)}>
        {en.actions.triggerRenderFailure}
      </button>
    </section>
  );
}
