import { useId, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Drawer } from "@fleetops/ui";
import { apiClient, type components } from "@fleetops/api-client";
import { checked, defectApi } from "./defectApi";
import { defectCategoryLabel } from "./defectLabels";
import { useOnline } from "../shell/Shell";

export function LogDefect({
  vehicleId,
  onClose,
  onSaved,
}: {
  vehicleId?: string;
  onClose: () => void;
  onSaved: (id: string) => void;
}) {
  const id = useId();
  const online = useOnline();
  const client = useQueryClient();
  const [search, setSearch] = useState("");
  const [vehicle, setVehicle] = useState(vehicleId ?? "");
  const [photos, setPhotos] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const vehicles = useQuery({
    queryKey: ["log-defect-vehicles", search],
    queryFn: () =>
      apiClient
        .GET("/api/v1/fleet/vehicles", { params: { query: { search, limit: 50, includeRetired: false } } })
        .then(checked),
  });
  const [driverSearch, setDriverSearch] = useState("");
  const drivers = useQuery({
    queryKey: ["defect-drivers", driverSearch],
    queryFn: () => defectApi.drivers(driverSearch),
  });
  return (
    <Drawer open title="Log a report" closeLabel="Close report" onClose={onClose}>
      <form
        className="fleet-form defect-form"
        onSubmit={async (event) => {
          event.preventDefault();
          if (busy || !online) return;
          const form = new FormData(event.currentTarget);
          setBusy(true);
          setError("");
          try {
            const fileIds: string[] = [];
            for (const file of photos) {
              const grant = await apiClient
                .POST("/api/v1/fleet/defects/upload-links", {
                  body: { contentType: file.type, sizeBytes: file.size },
                })
                .then(checked);
              const response = await fetch(grant.url.url, {
                method: "PUT",
                body: file,
                headers: grant.requiredHeaders,
                credentials: "omit",
              });
              if (!response.ok) throw new Error("photo_upload_failed");
              fileIds.push(grant.fileId);
            }
            const result = await defectApi.log({
              vehicleId: vehicle,
              category: form.get("category") as components["schemas"]["DefectCategory"],
              severity: form.get("severity") as components["schemas"]["DefectSeverity"],
              description: String(form.get("description")).trim() || null,
              reportedBy: String(form.get("reportedBy")) || null,
              photos: fileIds,
            });
            await client.invalidateQueries();
            onSaved(result.id);
          } catch {
            setError("Report could not be saved. Check the fields and try again.");
          } finally {
            setBusy(false);
          }
        }}
      >
        {!vehicleId && (
          <>
            <label htmlFor={`${id}-search`}>Search vehicle plate</label>
            <input
              id={`${id}-search`}
              value={search}
              disabled={busy || !online}
              onChange={(event) => setSearch(event.target.value)}
            />
          </>
        )}
        <label htmlFor={`${id}-vehicle`}>Vehicle</label>
        <select
          id={`${id}-vehicle`}
          value={vehicle}
          required
          disabled={busy || !online || !!vehicleId}
          onChange={(event) => setVehicle(event.target.value)}
        >
          <option value="">Choose vehicle</option>
          {vehicleId && !vehicles.data?.items.some((item) => item.id === vehicleId) && (
            <option value={vehicleId}>This vehicle</option>
          )}
          {vehicles.data?.items.map((item) => (
            <option key={item.id} value={item.id}>
              {item.plate}
            </option>
          ))}
        </select>
        <label htmlFor={`${id}-category`}>Category</label>
        <select id={`${id}-category`} name="category" disabled={busy || !online}>
          {(["brakes", "lights", "tyres", "engine", "body", "tacho", "load_securing", "other"] as const).map(
            (value) => (
              <option key={value} value={value}>
                {defectCategoryLabel(value)}
              </option>
            ),
          )}
        </select>
        <label htmlFor={`${id}-severity`}>Severity</label>
        <select id={`${id}-severity`} name="severity" defaultValue="major" disabled={busy || !online}>
          <option value="minor">Minor</option>
          <option value="major">Major</option>
          <option value="critical">Critical</option>
        </select>
        <label htmlFor={`${id}-description`}>Description</label>
        <textarea
          id={`${id}-description`}
          name="description"
          maxLength={4000}
          disabled={busy || !online}
          required={!photos.length}
        />
        <label htmlFor={`${id}-driver-search`}>Search driver</label>
        <input
          id={`${id}-driver-search`}
          type="search"
          value={driverSearch}
          disabled={busy || !online}
          onChange={(event) => setDriverSearch(event.target.value)}
        />
        <label htmlFor={`${id}-by`}>Reported by</label>
        <select id={`${id}-by`} name="reportedBy" disabled={busy || !online}>
          <option value="">Me (dispatcher)</option>
          {drivers.data?.items.map((driver) => (
            <option key={driver.id} value={driver.id}>
              {driver.name}
            </option>
          ))}
        </select>
        <label htmlFor={`${id}-photos`}>Photos (optional, up to 5)</label>
        <input
          id={`${id}-photos`}
          type="file"
          multiple
          accept="image/jpeg,image/png"
          disabled={busy || !online}
          onChange={(event) => {
            const files = Array.from(event.target.files ?? []);
            if (
              files.length > 5 ||
              files.some(
                (file) => !["image/jpeg", "image/png"].includes(file.type) || file.size > 20 * 1024 * 1024,
              )
            ) {
              setError("Choose up to 5 JPEG or PNG photos smaller than 20 MB each.");
              event.target.value = "";
              setPhotos([]);
              return;
            }
            setPhotos(files);
          }}
        />
        {(vehicles.isError || drivers.isError) && (
          <p role="alert">Vehicle or driver choices could not be loaded. Close and try again.</p>
        )}
        {error && <p role="alert">{error}</p>}
        <button className="ui-button primary" disabled={busy || !online || !vehicle}>
          {busy ? "Saving..." : "Log report"}
        </button>
      </form>
    </Drawer>
  );
}
