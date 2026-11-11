import { CheckCircle, Circle, Clock, TriangleAlert, Wrench, XCircle } from "lucide-react";
import { useSession } from "../auth/AuthGate";

export function useDefectAccess() {
  const session = useSession();
  return {
    canRead: session.data?.roles.some((role) => role === "dispatcher" || role === "owner") === true,
    canWrite: session.data?.roles.includes("dispatcher") === true,
  };
}
export function DefectState({ value, label: customLabel }: { value: string; label?: string }) {
  const Icon =
    value === "critical"
      ? TriangleAlert
      : value === "in_repair" || value === "in_progress"
        ? Wrench
        : ["done", "fixed", "closed", "acknowledged"].includes(value)
          ? CheckCircle
          : value === "cancelled"
            ? XCircle
            : value === "major" || value === "scheduled"
              ? Clock
              : Circle;
  const label = customLabel ?? value.replaceAll("_", " ");
  return (
    <span className={`defect-state defect-${value}`}>
      <Icon size={16} aria-hidden />
      {label[0].toUpperCase() + label.slice(1)}
    </span>
  );
}
export function companyToday() {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Berlin",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}
export function defectDate(value: string, time = false) {
  const date = new Date(value.length === 10 ? `${value}T12:00:00Z` : value);
  const day = new Intl.DateTimeFormat("de-DE", {
    timeZone: "Europe/Berlin",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(date);
  return time ? `${day} ${companyTime(value)}` : day;
}
export function companyTime(value: string) {
  return new Intl.DateTimeFormat("en-GB", {
    timeZone: "Europe/Berlin",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}
