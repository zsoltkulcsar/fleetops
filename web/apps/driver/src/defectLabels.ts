// Words the driver sees for defect values (design/screens/driver.md D-03). Never show wire values.
const categoryLabels: Record<string, string> = {
  brakes: "Brakes",
  lights: "Lights",
  tyres: "Tyres",
  engine: "Engine",
  body: "Body",
  tacho: "Tacho",
  load_securing: "Load securing",
  other: "Other",
};
const severityLabels: Record<string, string> = {
  minor: "Can drive",
  major: "Fix soon",
  critical: "Do not drive",
};
const statusLabels: Record<string, string> = {
  open: "Open",
  acknowledged: "Acknowledged",
  in_repair: "In repair",
  fixed: "Fixed",
  closed: "Closed",
  cancelled: "Cancelled",
};

export const defectCategoryLabel = (value: string) => categoryLabels[value] ?? value;
export const defectSeverityLabel = (value: string) => severityLabels[value] ?? value;
export const defectStatusLabel = (value: string) => statusLabels[value] ?? value;
