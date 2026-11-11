import { personRoles, roleLabels, statusLabels, stateLabels } from "./labels";

export function historyLabel(value: string) {
  const words = value.replace(/([a-z])([A-Z])/g, "$1 $2").replaceAll("_", " ");
  return words.charAt(0).toUpperCase() + words.slice(1);
}

export function historyValue(value: string | null) {
  if (value === null) return "\u2014";
  if (statusLabels[value] || stateLabels[value] || roleLabels[value])
    return statusLabels[value] ?? stateLabels[value] ?? roleLabels[value];
  if (value.startsWith("[")) {
    try {
      const roles: unknown = JSON.parse(value);
      if (
        Array.isArray(roles) &&
        roles.every((role): role is string => typeof role === "string" && role in roleLabels)
      )
        return personRoles(roles);
    } catch {
      /* Preserve an audit value that is not a role list. */
    }
  }
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return value.split("-").reverse().join(".");
  const labels: Record<string, string> = {
    true: "Yes",
    false: "No",
    requested: "Requested",
    sent: "Sent",
    full_time: "Full time",
    part_time: "Part time",
    minijob: "Minijob",
    de: "German",
    en: "English",
    ro: "Romanian",
    hu: "Hungarian",
    pl: "Polish",
  };
  return labels[value] ?? value;
}
