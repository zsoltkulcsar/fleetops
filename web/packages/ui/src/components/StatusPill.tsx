import {
  CalendarDays,
  Check,
  CheckCircle2,
  CircleDashed,
  Clock3,
  Hourglass,
  Info,
  Lock,
  OctagonX,
  Pencil,
  Play,
  TriangleAlert,
  Wrench,
  X,
} from "lucide-react";
import type { ComponentType } from "react";
import { formatBerlinDate } from "../date";

type StatusIcon = ComponentType<{ size?: number; "aria-hidden"?: boolean }>;
export type StatusDefinition = { label: string; tone: string; icon: StatusIcon };
export type AlertSeverity = "critical" | "warning" | "info";
export const statusVocabulary = {
  valid: { label: "Valid", tone: "ok", icon: CheckCircle2 },
  verified: { label: "Valid", tone: "ok", icon: CheckCircle2 },
  expiring: { label: "Expires", tone: "warn", icon: Clock3 },
  expired: { label: "Expired", tone: "crit", icon: OctagonX },
  missing: { label: "Missing", tone: "idle", icon: CircleDashed },
  uploaded: { label: "Awaiting verification", tone: "warn", icon: Hourglass },
  rejected: { label: "Rejected", tone: "crit", icon: OctagonX },
  "shift-rejected": { label: "Rejected", tone: "crit", icon: X },
  usable: { label: "Usable", tone: "ok", icon: CheckCircle2 },
  "usable-warning": { label: "Usable, warning", tone: "warn", icon: Clock3 },
  "off-road": { label: "Off road", tone: "crit", icon: TriangleAlert },
  maintenance: { label: "In workshop", tone: "idle", icon: Wrench },
  "no-driver": { label: "No driver", tone: "idle", icon: CircleDashed },
  running: { label: "Running", tone: "accent", icon: Play },
  draft: { label: "Draft", tone: "idle", icon: Pencil },
  submitted: { label: "Awaiting approval", tone: "warn", icon: Hourglass },
  approved: { label: "Approved", tone: "ok", icon: Check },
  exported: { label: "Exported", tone: "ok", icon: Lock },
  "missing-day": { label: "Missing", tone: "crit", icon: CircleDashed },
  absence: { label: "Vacation / Sick / …", tone: "idle", icon: CalendarDays },
  "rule-warning": { label: "Possible breach", tone: "warn", icon: Clock3 },
  blocked: { label: "Blocked", tone: "crit", icon: OctagonX },
  "on-shift": { label: "On shift", tone: "ok", icon: Check },
  "not-started": { label: "Not started", tone: "accent", icon: Play },
  absent: { label: "Absent", tone: "idle", icon: CircleDashed },
  onboarding: { label: "Onboarding", tone: "idle", icon: Info },
  critical: { label: "Critical", tone: "crit", icon: OctagonX },
  warning: { label: "Warning", tone: "warn", icon: Clock3 },
  info: { label: "Notice", tone: "accent", icon: Info },
} satisfies Record<string, StatusDefinition>;
export type StatusName = keyof typeof statusVocabulary;
export type StatusDate = string | Date;
export function formatStatusDate(date: StatusDate): string | null {
  const value = formatBerlinDate(date, true);
  return value ? `${value}.` : null;
}
type DatedStatusProps = { status: "expiring" | "expired"; date: StatusDate; label?: string };
type AbsenceProps = { status: "absence"; label: string; date?: never };
type OtherStatusProps = {
  status: Exclude<StatusName, "expiring" | "expired" | "absence">;
  label?: string;
  date?: StatusDate;
};
export function StatusPill(props: DatedStatusProps | AbsenceProps | OtherStatusProps) {
  const definition = statusVocabulary[props.status];
  if (!definition)
    return (
      <span className="ui-status status-idle" data-status="unknown">
        <span>{props.label ?? "Unknown status"}</span>
      </span>
    );
  const dateLabel =
    props.status === "expiring" || props.status === "expired" ? formatStatusDate(props.date) : null;
  const datedLabel = dateLabel
    ? `${props.status === "expiring" ? "Expires" : "Expired"} ${dateLabel}`
    : props.status === "expiring"
      ? "Expires"
      : props.status === "expired"
        ? "Expired"
        : definition.label;
  const Icon = definition.icon;
  const text = props.label ?? datedLabel;
  return (
    <span className={`ui-status status-${definition.tone}`} data-status={props.status}>
      <Icon size={14} aria-hidden />
      <span>{text}</span>
    </span>
  );
}
