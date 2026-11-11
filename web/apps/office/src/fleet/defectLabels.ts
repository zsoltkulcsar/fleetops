// Words the office sees for defect values (design/screens/c-fleet.md O-08). Never show wire values.
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
  minor: "Minor",
  major: "Major",
  critical: "Critical",
};

export const defectCategoryLabel = (value: string) => categoryLabels[value] ?? value;
export const defectSeverityLabel = (value: string) => severityLabels[value] ?? value;
