import { en } from "../i18n/en";
import type { ReadinessCause } from "./api";

function documentLabel(documentType: string | null, fallback: string): string {
  if (!documentType) return fallback;
  const labels = en.documents.types as Record<string, string>;
  return labels[documentType] ?? fallback;
}

export function peopleCauseText(cause: ReadinessCause): string | null {
  if (cause.code === "not_a_driver") return null;
  if (cause.code === "suspended") return "Blocked — suspended";
  if (cause.code === "left") return "Blocked — left the company";
  if (cause.code === "document_expired") {
    return `Blocked — ${documentLabel(cause.documentType, "document")} expired`;
  }
  if (cause.code === "document_missing") {
    return `Blocked — ${documentLabel(cause.documentType, "document")} missing`;
  }
  return "Blocked — document expired";
}

export function fleetCauseText(cause: ReadinessCause): string {
  if (cause.code === "manual_off_road") return "Set off road by dispatch";
  if (cause.code === "document_expired") {
    return `${documentLabel(cause.documentType, "Document")} expired`;
  }
  if (cause.code === "document_missing") {
    return `${documentLabel(cause.documentType, "Document")} missing`;
  }
  if (cause.code === "retired") return "Retired";
  return "Not usable";
}
