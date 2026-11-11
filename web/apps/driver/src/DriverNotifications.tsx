import { apiClient } from "@fleetops/api-client";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";

export function DriverNotifications({ online }: { online: boolean }) {
  const notifications = useQuery({
    queryKey: ["driver-notifications"],
    enabled: online,
    queryFn: async () => {
      const result = await apiClient.GET("/api/v1/notifications", {
        params: { query: { limit: 10 } },
        cache: "no-store",
      });
      if (!result.response.ok || !result.data) throw new Error("notifications_unavailable");
      return result.data;
    },
    refetchInterval: 60_000,
  });
  const unread = notifications.data?.items.filter((item) => !item.readAt) ?? [];
  if (!online || !unread.length) return null;
  return (
    <aside className="driver-notifications" aria-label="Notifications">
      {unread.map((item) => (
        <div className="driver-callout" key={item.id}>
          <p>{item.text}</p>
          <Link
            to={
              item.templateCode === "document_rejected" || item.templateCode === "vacation_decided"
                ? "/me"
                : "/week"
            }
            onClick={async () => {
              await apiClient.POST("/api/v1/notifications/{id}/read", { params: { path: { id: item.id } } });
              void notifications.refetch();
            }}
          >
            Open{" "}
            {item.templateCode === "document_rejected"
              ? "document"
              : item.templateCode === "vacation_decided"
                ? "vacation requests"
                : "schedule"}
          </Link>
        </div>
      ))}
    </aside>
  );
}
