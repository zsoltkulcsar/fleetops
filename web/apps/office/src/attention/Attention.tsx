import { RuleAcknowledgement } from "./RuleAcknowledgement";
import { Corrections } from "../timesheets/Corrections";
import { TachoDownload } from "../fleet/TachoDownload";
import { useEffect, useId, useMemo, useState } from "react";
import { CalendarDays, Check, ChevronDown, CircleAlert, Clock3, Info, Truck, UserRound } from "lucide-react";
import { apiClient } from "@fleetops/api-client";
import { documentLabel } from "../people/labels";
import { checked } from "../people/api";
import { ReleaseReview } from "./ReleaseReview";
import { PlanningActions } from "./PlanningActions";
import "../people/people.css";
import { Banner, Plate, SegmentedControl, Skeleton, Toast } from "@fleetops/ui";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router";
import { useSession } from "../auth/AuthGate";
import { en } from "../i18n/en";
import { formatDocumentDate, formatTodayBerlin } from "../shell/date";
import { useOnline } from "../shell/Shell";
import { attentionApi, type AttentionGroup, type AttentionItem } from "./api";
import "./attention.css";

const severityRank = { critical: 2, warning: 1, info: 0 } as const;
function itemTitle(item: AttentionItem) {
  if (item.ruleId === "A-11") return "Driving guidance needs review";
  if (item.ruleId === "A-14") return "Correction requested";
  if (item.ruleId === "A-06") return "Maintenance due";
  if (item.ruleId === "A-12") return "Tacho download overdue";
  if (item.ruleId === "A-01") return "Driver document expiring";
  if (item.ruleId === "A-04") return "Vehicle document expiring";
  if (item.ruleId === "A-13") return "Office override used";
  if (item.ruleId === "A-07") return "Critical defect: vehicle off road";
  if (item.ruleId === "A-08") return "Major defect awaiting repair";
  if (item.ruleId === "A-21") return "Schedule clash";
  if (item.ruleId === "A-18") return "Worked during absence";
  if (item.ruleId === "A-19") return "Schedule change requested";
  // Titles from the alert catalogue (design/domain.md section 13), never the bare code.
  if (item.ruleId === "A-09") return "Missing timesheet";
  if (item.ruleId === "A-10") return "Shift waiting for approval";
  if (item.ruleId === "A-15") return "Week not submitted";
  if (item.ruleId === "A-16") return "Shift running too long";
  if (item.ruleId === "A-17") return "Driver change on a locked shift";
  if (item.ruleId === "A-20") return "Cycle overrun: handover late";
  if (item.ruleId === "A-02") return `${en.attention.rules.a02} \u00b7 ${documentLabel(item.scopeKey ?? "")}`;
  if (item.ruleId === "A-03") return `${en.attention.rules.a03} \u00b7 ${documentLabel(item.scopeKey ?? "")}`;
  if (item.ruleId === "A-05") return `${en.attention.rules.a05} \u00b7 ${documentLabel(item.scopeKey ?? "")}`;
  return item.ruleId;
}
function actionFor(item: AttentionItem, group: AttentionGroup, owner: boolean, dispatcherOnly: boolean) {
  if (item.ruleId === "A-06")
    return {
      label: "Schedule",
      href: `/fleet/maintenance?vehicleId=${group.subjectId}&action=plan&maintenanceId=${encodeURIComponent(item.scopeKey ?? "")}`,
    };
  if (item.ruleId === "A-01")
    return dispatcherOnly
      ? {
          label: "Plan truck reassignment",
          href: `/schedule?search=${encodeURIComponent(group.subjectName ?? "")}`,
        }
      : { label: "Upload renewal", href: `/documents?upload=employee:${group.subjectId}:${item.scopeKey}` };
  if (item.ruleId === "A-04")
    return owner
      ? { label: "Open vehicle", href: `/fleet/${group.subjectId}` }
      : { label: "Upload renewal", href: `/documents?upload=vehicle:${group.subjectId}:${item.scopeKey}` };
  if (item.ruleId === "A-07" || item.ruleId === "A-08")
    return {
      label: owner ? "View defect" : "Open defect",
      href: `/fleet/defects?defectId=${group.subjectId}`,
    };
  if (item.ruleId === "A-10")
    return { label: "Review & approve", href: `/timesheets?shiftId=${group.subjectId}` };
  if (item.ruleId === "A-16" || item.ruleId === "A-17")
    return { label: "Open shift", href: `/timesheets?shiftId=${group.subjectId}` };
  if (item.ruleId === "A-09") {
    const day = /^\d{4}-\d{2}-\d{2}$/.test(item.scopeKey ?? "")
      ? item.scopeKey
      : (item.dueAt ?? "").slice(0, 10);
    return {
      label: "Add shift for driver",
      href: `/timesheets?filter=missing&search=${encodeURIComponent(group.subjectName ?? "")}${day ? `&week=${day}` : ""}`,
    };
  }
  if (item.ruleId === "A-15")
    return {
      label: "Open shifts",
      href: `/timesheets?search=${encodeURIComponent(group.subjectName ?? "")}`,
    };
  if (item.ruleId === "A-20")
    return {
      label: "Open Schedule",
      href: `/schedule?cycleId=${group.subjectId}${item.dueAt ? `&from=${item.dueAt.slice(0, 10)}` : ""}`,
    };
  if (item.ruleId === "A-21")
    return {
      label: "Open Schedule",
      href: `/schedule?cycleId=${group.subjectId}${item.dueAt ? `&from=${item.dueAt.slice(0, 10)}` : ""}`,
    };
  if (owner)
    return {
      label: en.attention.view,
      href: item.ruleId === "A-05" ? `/fleet/${group.subjectId}` : "/documents",
    };
  if (item.ruleId === "A-02")
    return {
      label: en.attention.actions.upload,
      href: `/documents?upload=employee:${group.subjectId}:${item.scopeKey}`,
    };
  if (item.ruleId === "A-03") return { label: en.attention.actions.verify, href: "/documents?filter=queue" };
  return {
    label: en.attention.actions.renew,
    href: `/documents?upload=vehicle:${group.subjectId}:${item.scopeKey}`,
  };
}

export function Attention() {
  const session = useSession();
  const online = useOnline();
  const client = useQueryClient();
  const navigate = useNavigate();
  const owner = session.data?.roles.includes("owner") === true;
  const canManagePeople = session.data?.roles.some((role) => role === "hr" || role === "owner") === true;
  const dispatcherOnly = session.data?.roles.includes("dispatcher") === true && !canManagePeople;
  const [reviewing, setReviewing] = useState<{ item: AttentionItem; group: AttentionGroup } | null>(null);
  const canSnooze = session.data?.roles.some((role) => role === "hr" || role === "dispatcher") === true;
  const [filter, setFilter] = useState("all");
  const [visibleCount, setVisibleCount] = useState(8);
  const [snoozing, setSnoozing] = useState<{ item: AttentionItem; group: AttentionGroup } | null>(null);
  const [toast, setToast] = useState<{ id: string; version: number } | null>(null);
  const query = useQuery({ queryKey: ["attention"], queryFn: attentionApi.list, refetchInterval: 60_000 });
  const groups = useMemo(
    () =>
      [...(query.data?.groups ?? [])]
        .filter((group) => filter === "all" || group.worstSeverity === filter)
        .sort((left, right) => severityRank[right.worstSeverity] - severityRank[left.worstSeverity]),
    [filter, query.data?.groups],
  );
  const readiness = useQuery({
    queryKey: ["readiness-summary"],
    queryFn: () => apiClient.GET("/api/v1/readiness/summary").then(checked),
    enabled: owner || Boolean(session.data),
    refetchInterval: 60_000,
  });
  useEffect(() => {
    document.title = `${en.nav.attention} \u00b7 ${en.appName}`;
  }, []);
  const visibleGroups = groups.slice(0, visibleCount);
  useEffect(() => setVisibleCount(8), [filter]);
  useEffect(() => {
    if (query.isSuccess) void client.invalidateQueries({ queryKey: ["attention"] });
  }, []);
  if (query.isLoading)
    return (
      <div className="page attention-page">
        <Skeleton />
        <Skeleton />
        <Skeleton />
      </div>
    );
  if (query.isError)
    return (
      <div className="page attention-page">
        <Banner
          kind="error"
          label={en.attention.error}
          retryLabel={en.attention.retry}
          onRetry={() => void query.refetch()}
        >
          {en.attention.errorBody}
        </Banner>
      </div>
    );
  const data = query.data;
  if (!data) return null;
  if (data.companyEmpty)
    return (
      <div className="page attention-page">
        <header className="page-title">
          <div>
            <span className="eyebrow">{en.nav.attention}</span>
            <h1 tabIndex={-1}>{en.attention.title}</h1>
            <p className="ui-muted">{en.attention.subtitle}</p>
          </div>
        </header>
        {!online && (
          <Banner kind="offline" label={en.banner.offline}>
            {en.states.offline}
          </Banner>
        )}
        <section className="attention-setup">
          <Truck size={28} aria-hidden />
          <h2>{en.attention.setup.title}</h2>
          <p>{en.attention.setup.body}</p>
          <div>
            <Link className="ui-button primary" to="/fleet">
              {en.attention.setup.vehicle}
            </Link>
            <Link className="ui-button" to="/documents">
              {en.attention.setup.document}
            </Link>
          </div>
        </section>
      </div>
    );
  return (
    <div className="page attention-page">
      <header className="page-title">
        <div>
          <span className="eyebrow" data-testid="today">
            {en.nav.attention} {"\u00b7"} {formatTodayBerlin()}
          </span>
          <h1 tabIndex={-1}>{en.attention.title}</h1>
          <p className="ui-muted">{en.attention.openLine(data.totals.open, data.totals.critical)}</p>
        </div>
      </header>
      {!online && (
        <Banner kind="offline" label={en.banner.offline}>
          {en.states.offline}
        </Banner>
      )}
      {owner && (
        <div className="attention-kpis">
          <button type="button" onClick={() => setFilter("all")}>
            <strong>{data.totals.open}</strong>
            <span>{en.attention.open}</span>
          </button>
          <button type="button" onClick={() => setFilter("critical")}>
            <strong>{data.totals.critical}</strong>
            <span>{en.attention.critical}</span>
          </button>
          <button type="button" onClick={() => navigate("/fleet")}>
            <strong>{readiness.data?.vehiclesUsable ?? "-"}</strong>
            <span>{en.attention.usable}</span>
          </button>
        </div>
      )}
      <div className="attention-toolbar">
        <h2>{en.attention.queueTitle}</h2>
        <SegmentedControl
          ariaLabel={en.attention.filterLabel}
          value={filter}
          onChange={setFilter}
          options={[
            { id: "all", label: en.attention.all },
            { id: "critical", label: en.attention.critical },
            { id: "warning", label: en.attention.warnings },
          ]}
        />
        <Link to="/fleet" className="attention-readiness">
          {en.attention.readiness(readiness.data?.vehiclesUsable ?? 0, readiness.data?.vehiclesTotal ?? 0)}
        </Link>
      </div>
      {groups.length === 0 ? (
        <section className="attention-empty">
          <Check size={28} aria-hidden />
          <h2>{en.attention.empty}</h2>
        </section>
      ) : (
        <div className="attention-groups">
          {visibleGroups.map((group) => (
            <AttentionCard
              key={`${group.subjectType}-${group.subjectId}`}
              group={group}
              owner={owner}
              canSnooze={canSnooze}
              canManagePeople={canManagePeople}
              dispatcherOnly={dispatcherOnly}
              onReview={setReviewing}
              onSnooze={setSnoozing}
            />
          ))}
          {groups.length > visibleCount && (
            <button
              className="attention-show-more"
              type="button"
              onClick={() => setVisibleCount((count) => count + 8)}
            >
              <ChevronDown size={16} aria-hidden />
              {en.attention.more(Math.min(8, groups.length - visibleCount))}
            </button>
          )}
        </div>
      )}
      {reviewing && (
        <ReleaseReview
          group={reviewing.group}
          item={reviewing.item}
          onClose={() => setReviewing(null)}
          onSaved={() => {
            setReviewing(null);
            void query.refetch();
          }}
        />
      )}
      {snoozing && (
        <SnoozeDialog
          group={snoozing.group}
          item={snoozing.item}
          onClose={() => setSnoozing(null)}
          onSaved={(id, version) => {
            setSnoozing(null);
            setToast({ id, version });
            void query.refetch();
          }}
        />
      )}
      <Toast
        open={toast !== null}
        onClose={() => setToast(null)}
        undo={() => {
          if (toast)
            void attentionApi.unsnooze(toast.id, toast.version).then(() => {
              setToast(null);
              void query.refetch();
            });
        }}
        dismissLabel={en.actions.close}
        undoLabel={en.actions.undo}
      >
        {en.attention.snoozed}
      </Toast>
    </div>
  );
}

function AttentionCard({
  group,
  owner,
  canSnooze,
  canManagePeople,
  dispatcherOnly,
  onReview,
  onSnooze,
}: {
  group: AttentionGroup;
  owner: boolean;
  canSnooze: boolean;
  canManagePeople: boolean;
  dispatcherOnly: boolean;
  onReview: (value: { item: AttentionItem; group: AttentionGroup }) => void;
  onSnooze: (value: { item: AttentionItem; group: AttentionGroup }) => void;
}) {
  const online = useOnline();
  const [expanded, setExpanded] = useState(false);
  const actionsId = useId();
  const critical = group.worstSeverity === "critical";
  return (
    <article className={`attention-card ${critical ? "critical" : "compact"}`}>
      <header>
        <span className="eyebrow">
          {group.subjectType === "cycle"
            ? "Schedule"
            : group.subjectType === "vehicle" || group.subjectType === "defect"
              ? en.attention.vehicle
              : en.attention.person}
        </span>
        <span className="attention-subject">
          {group.subjectType === "vehicle" || group.subjectType === "defect" ? (
            <Truck size={17} aria-hidden />
          ) : group.subjectType === "cycle" ? (
            <CalendarDays size={17} aria-hidden />
          ) : (
            <UserRound size={17} aria-hidden />
          )}
          {group.subjectType === "vehicle" || group.subjectType === "defect" ? (
            <Plate>{group.subjectName}</Plate>
          ) : (
            group.subjectName
          )}
        </span>
      </header>
      <div className="attention-body">
        <div className="attention-items">
          {group.items.map((item) => {
            const action = actionFor(item, group, owner, dispatcherOnly);
            const SeverityIcon =
              item.severity === "critical" ? CircleAlert : item.severity === "warning" ? Clock3 : Info;
            return (
              <div className="attention-item" key={item.id}>
                <span className={`attention-pill ${item.severity}`}>
                  <SeverityIcon size={14} aria-hidden />
                  {item.severity === "critical"
                    ? en.attention.critical
                    : item.severity === "warning"
                      ? en.attention.warning
                      : en.attention.notice}
                </span>
                <div className="attention-item-copy">
                  <h3>
                    {itemTitle(item)} <span className="attention-rule">{item.ruleId}</span>
                  </h3>
                  {item.guidance && <p className="ui-muted">{item.guidance}</p>}
                  <p className="ui-muted">
                    {item.dueAt ? (
                      <>
                        {en.attention.due} <time dateTime={item.dueAt}>{formatDocumentDate(item.dueAt)}</time>
                      </>
                    ) : (
                      en.attention.noDueDate
                    )}
                  </p>
                </div>
                <div className="attention-item-actions">
                  {item.ruleId === "A-14" ? (
                    <Corrections requestId={group.subjectId} />
                  ) : item.ruleId === "A-11" ? (
                    <>
                      <Link className="ui-button" to={`/timesheets?shiftId=${group.subjectId}`}>
                        Open shift
                      </Link>
                      <RuleAcknowledgement item={item} />
                    </>
                  ) : item.ruleId === "A-12" ? (
                    <TachoDownload
                      subjectId={group.subjectId}
                      subjectType={group.subjectType === "vehicle" ? "vehicle" : "employee"}
                    />
                  ) : item.ruleId === "A-18" || item.ruleId === "A-19" ? (
                    <PlanningActions item={item} group={group} />
                  ) : item.ruleId === "A-13" && canManagePeople ? (
                    <button
                      type="button"
                      className="ui-button primary"
                      disabled={!online}
                      onClick={() => onReview({ item, group })}
                    >
                      Review
                    </button>
                  ) : (
                    <Link className="ui-button primary" to={action.href}>
                      {action.label}
                    </Link>
                  )}
                  {item.ruleId === "A-06" && (
                    <Link
                      className="ui-button"
                      to={`/fleet/maintenance?vehicleId=${group.subjectId}&action=done&maintenanceId=${encodeURIComponent(item.scopeKey ?? "")}`}
                    >
                      Mark done
                    </Link>
                  )}
                  {item.ruleId === "A-04" && !owner && (
                    <Link
                      className="ui-button"
                      to={`/fleet/maintenance?vehicleId=${group.subjectId}&action=plan&type=${({ hu_report: "inspection_hu", sp_report: "inspection_sp", tacho_calibration: "tacho_check" } as Record<string, string>)[item.scopeKey ?? ""] ?? "other"}`}
                    >
                      Plan maintenance
                    </Link>
                  )}
                  {item.ruleId === "A-02" && canManagePeople && (
                    <Link className="ui-button" to={`/people/${group.subjectId}?action=suspend`}>
                      Suspend driver
                    </Link>
                  )}
                  {item.ruleId === "A-04" && !owner && (
                    <Link className="ui-button" to={`/fleet/${group.subjectId}`}>
                      Open vehicle
                    </Link>
                  )}
                  {!owner && item.ruleId === "A-07" && (
                    <>
                      <Link
                        className="ui-button"
                        to={`/fleet/defects?defectId=${group.subjectId}&action=repair`}
                      >
                        Create repair
                      </Link>
                      <Link
                        className="ui-button"
                        to={`/fleet/defects?defectId=${group.subjectId}&action=swap`}
                      >
                        Swap driver
                      </Link>
                    </>
                  )}
                  {!owner && item.ruleId === "A-08" && (
                    <Link
                      className="ui-button"
                      to={`/fleet/defects?defectId=${group.subjectId}&action=repair`}
                    >
                      Create repair
                    </Link>
                  )}
                  {expanded &&
                    canSnooze &&
                    !(dispatcherOnly && item.ruleId === "A-01") &&
                    item.ruleId !== "A-07" &&
                    item.snoozable && (
                      <button
                        className="ui-button ghost"
                        type="button"
                        onClick={() => onSnooze({ item, group })}
                      >
                        {en.attention.snooze}
                      </button>
                    )}
                </div>
              </div>
            );
          })}
        </div>
        {!critical && (
          <>
            <button
              className="more-actions"
              type="button"
              aria-expanded={expanded}
              aria-controls={actionsId}
              onClick={() => setExpanded((value) => !value)}
            >
              <ChevronDown size={15} aria-hidden />
              {en.attention.moreActions}
            </button>
            <div id={actionsId} className="attention-secondary" hidden={!expanded}>
              {group.subjectType === "vehicle" && (
                <Link to={`/fleet/${group.subjectId}`}>{en.attention.viewVehicle}</Link>
              )}
              <Link to="/documents?filter=all">{en.attention.viewDocuments}</Link>
              <Link to="/documents?filter=queue">{en.attention.reviewQueue}</Link>
            </div>
          </>
        )}
      </div>
    </article>
  );
}

function SnoozeDialog({
  item,
  onClose,
  onSaved,
}: {
  group: AttentionGroup;
  item: AttentionItem;
  onClose: () => void;
  onSaved: (id: string, version: number) => void;
}) {
  const fieldId = useId();
  const [until, setUntil] = useState(() => new Date(Date.now() + 86_400_000).toISOString().slice(0, 10));
  const [reason, setReason] = useState("");
  const [error, setError] = useState("");
  return (
    <div className="dialog-backdrop">
      <form
        className="attention-dialog"
        onSubmit={(event) => {
          event.preventDefault();
          if (!reason.trim()) {
            setError(en.attention.reasonRequired);
            return;
          }
          void attentionApi
            .snooze(item.id, {
              version: item.version,
              until: new Date(`${until}T23:59:59.000Z`).toISOString(),
              reason,
            })
            .then((result) => onSaved(item.id, result.version))
            .catch(() => setError(en.attention.snoozeError));
        }}
      >
        <h2>{en.attention.snoozeTitle}</h2>
        <label htmlFor={`${fieldId}-until`}>{en.attention.until}</label>
        <input
          id={`${fieldId}-until`}
          type="date"
          value={until}
          min={new Date().toISOString().slice(0, 10)}
          max={new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 10)}
          onChange={(event) => setUntil(event.target.value)}
        />
        <label htmlFor={`${fieldId}-reason`}>{en.attention.reason}</label>
        <textarea
          id={`${fieldId}-reason`}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
        />
        {error && <p className="ui-muted">{error}</p>}
        <div>
          <button type="button" className="ui-button" onClick={onClose}>
            {en.actions.close}
          </button>
          <button type="submit" className="ui-button primary">
            {en.attention.snooze}
          </button>
        </div>
      </form>
    </div>
  );
}
