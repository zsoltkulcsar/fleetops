import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, Check, ChevronDown, CircleAlert, FileUp, Truck, UserRound } from "lucide-react";
import { Banner, SegmentedControl, Skeleton, Toast } from "@fleetops/ui";
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
  if (item.ruleId === "A-02")
    return `${en.attention.rules.a02} · ${en.documents.types[item.scopeKey as keyof typeof en.documents.types] ?? item.scopeKey}`;
  if (item.ruleId === "A-03")
    return `${en.attention.rules.a03} · ${en.documents.types[item.scopeKey as keyof typeof en.documents.types] ?? item.scopeKey}`;
  if (item.ruleId === "A-05")
    return `${en.attention.rules.a05} · ${en.documents.types[item.scopeKey as keyof typeof en.documents.types] ?? item.scopeKey}`;
  return item.ruleId;
}
function actionFor(item: AttentionItem, group: AttentionGroup, owner: boolean) {
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
  const canSnooze = session.data?.roles.some((role) => role === "hr" || role === "dispatcher") === true;
  const [filter, setFilter] = useState("all");
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
    queryFn: async () =>
      (await (await fetch("/api/v1/readiness/summary")).json()) as {
        vehiclesUsable: number;
        vehiclesTotal: number;
      },
    enabled: owner || Boolean(session.data),
    refetchInterval: 60_000,
  });
  useEffect(() => {
    document.title = `${en.nav.attention} · ${en.appName}`;
  }, []);
  const visibleGroups = groups.slice(0, 8);
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
            {en.nav.attention} · {formatTodayBerlin()}
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
              onSnooze={setSnoozing}
            />
          ))}
          {groups.length > 8 && <p className="ui-muted">{en.attention.more(groups.length - 8)}</p>}
        </div>
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
  onSnooze,
}: {
  group: AttentionGroup;
  owner: boolean;
  canSnooze: boolean;
  onSnooze: (value: { item: AttentionItem; group: AttentionGroup }) => void;
}) {
  const navigate = useNavigate();
  const critical = group.worstSeverity === "critical";
  return (
    <article className={`attention-card ${critical ? "critical" : "compact"}`}>
      <header>
        <span className="attention-subject">
          {group.subjectType === "vehicle" ? (
            <Truck size={17} aria-hidden />
          ) : (
            <UserRound size={17} aria-hidden />
          )}
          {group.subjectName}
        </span>
        <span className={`ui-status status-${critical ? "crit" : "warn"}`}>
          {critical ? en.attention.critical : en.attention.warning}
        </span>
      </header>
      <div className="attention-items">
        {group.items.map((item) => {
          const action = actionFor(item, group, owner);
          return (
            <div className="attention-item" key={item.id}>
              <span className={`attention-pill ${item.severity}`}>
                <CircleAlert size={14} aria-hidden />
                {item.severity === "critical" ? en.attention.critical : en.attention.warning}
              </span>
              <div>
                <h2>{itemTitle(item)}</h2>
                <p className="ui-muted">
                  {item.dueAt ? formatDocumentDate(item.dueAt) : en.attention.noDueDate}
                </p>
              </div>
              <Link className="ui-button primary" to={action.href}>
                {action.label}
              </Link>
              {canSnooze && item.snoozable && (
                <button className="ui-button ghost" type="button" onClick={() => onSnooze({ item, group })}>
                  {en.attention.snooze}
                </button>
              )}
            </div>
          );
        })}
      </div>
      {!critical && (
        <button
          className="more-actions"
          type="button"
          onClick={() => group.items[0]?.ruleId === "A-05" && navigate(`/fleet/${group.subjectId}`)}
        >
          <ChevronDown size={15} aria-hidden />
          {en.attention.moreActions}
        </button>
      )}
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
            .then((result) => onSaved(item.id, (result as { version: number }).version))
            .catch(() => setError(en.attention.snoozeError));
        }}
      >
        <h2>{en.attention.snoozeTitle}</h2>
        <label>
          {en.attention.until}
          <input
            type="date"
            value={until}
            min={new Date().toISOString().slice(0, 10)}
            max={new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 10)}
            onChange={(event) => setUntil(event.target.value)}
          />
        </label>
        <label>
          {en.attention.reason}
          <textarea value={reason} onChange={(event) => setReason(event.target.value)} />
        </label>
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
