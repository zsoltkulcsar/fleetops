import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { KpiTile } from "@fleetops/ui";
import { apiClient, type components } from "@fleetops/api-client";

type Kpi = components["schemas"]["OwnerKpi"];

const exportStatusLabels: Record<string, string> = {
  not_generated: "Not generated",
  generated: "Generated, not sent",
  sent: "Sent",
  discarded: "Discarded",
  superseded: "Superseded",
};

function label(kpi: Kpi): { title: string; value: string } {
  switch (kpi.key) {
    case "criticalAlerts":
      return { title: "Critical alerts", value: String(kpi.value ?? 0) };
    case "trucksOffRoad":
      return { title: "Trucks off road", value: String(kpi.value ?? 0) };
    case "documentsCompletePercent":
      return {
        title: "Documents complete",
        value: kpi.value === null || kpi.value === undefined ? "—" : `${Math.round(kpi.value)} %`,
      };
    case "exportStatus":
      return { title: "Last month's export", value: exportStatusLabels[kpi.status ?? ""] ?? "Not generated" };
    case "maintenanceCostEur":
      return {
        title: "Maintenance cost this month",
        value: new Intl.NumberFormat("de-DE", { style: "currency", currency: "EUR" }).format(kpi.value ?? 0),
      };
    default:
      return {
        title: kpi.key,
        value: kpi.value === null || kpi.value === undefined ? "—" : String(kpi.value),
      };
  }
}

/** Owner totals above Attention (Story 12.6). Company totals only: no hours per named driver. */
export function OwnerStrip() {
  const navigate = useNavigate();
  const overview = useQuery({
    queryKey: ["owner-overview"],
    queryFn: async () => {
      const result = await apiClient.GET("/api/v1/owner/overview");
      if (result.error || !result.data) throw new Error("owner_overview_unavailable");
      return result.data;
    },
    retry: false,
  });
  if (!overview.data) return null;
  return (
    <section className="owner-strip" aria-label="Company overview">
      {overview.data.items.map((kpi) => {
        const text = label(kpi);
        return (
          <KpiTile key={kpi.key} label={text.title} value={text.value} onClick={() => navigate(kpi.href)} />
        );
      })}
    </section>
  );
}
