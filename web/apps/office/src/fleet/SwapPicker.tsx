import { useId, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { Drawer } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import { defectApi } from "./defectApi";
import { companyTime, companyToday, defectDate } from "./defectPresentation";
import { useOnline } from "../shell/Shell";

export function SwapPicker({
  vehicleId,
  repair,
  onClose,
}: {
  vehicleId: string;
  repair?: components["schemas"]["RepairDetail"];
  onClose: () => void;
}) {
  const id = useId();
  const today = companyToday();
  const online = useOnline();
  const client = useQueryClient();
  const [driverSearch, setDriverSearch] = useState("");
  const drivers = useQuery({
    queryKey: ["defect-drivers", driverSearch],
    queryFn: () => defectApi.drivers(driverSearch),
  });
  const schedule = useQuery({
    queryKey: ["swap-current-drivers", vehicleId, today],
    queryFn: async () => {
      const { apiClient } = await import("@fleetops/api-client");
      const { checked } = await import("./defectApi");
      return apiClient
        .GET("/api/v1/schedule", { params: { query: { from: today, to: today, vehicleId } } })
        .then(checked);
    },
  });
  const [driver, setDriver] = useState("");
  const selectedDriver =
    driver || schedule.data?.swaps?.[0]?.employeeId || schedule.data?.items[0]?.employeeId || "";
  const [vehicle, setVehicle] = useState("");
  const [to, setTo] = useState(repair?.scheduledFor ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState<components["schemas"]["AssignmentDetail"]>();
  const options = useQuery({
    queryKey: ["swap-options", selectedDriver, today],
    queryFn: () => defectApi.swapOptions(selectedDriver, today),
    enabled: !!selectedDriver && online,
  });
  return (
    <Drawer open title="Swap driver" closeLabel="Close swap picker" onClose={onClose}>
      {saved ? (
        <div className="fleet-form">
          <p role="status">Swap saved. Affected drivers will be told.</p>
          <p>
            {defectDate(saved.from)} to {saved.to ? defectDate(saved.to) : "Open ended"}
          </p>
          <Link to="/schedule">Open Schedule</Link>
        </div>
      ) : (
        <form
          className="fleet-form defect-form"
          onSubmit={async (event) => {
            event.preventDefault();
            if (!online || !selectedDriver || !vehicle || busy) return;
            setBusy(true);
            setError("");
            try {
              const assignment = await defectApi.assign({
                employeeId: selectedDriver,
                vehicleId: vehicle,
                to: to || null,
                maintenanceId: repair?.id ?? null,
              });
              await client.invalidateQueries();
              setSaved(assignment);
            } catch {
              setError("Swap could not be saved. Refresh the available trucks and try again.");
            } finally {
              setBusy(false);
            }
          }}
        >
          <label htmlFor={`${id}-driver-search`}>Search driver</label>
          <input
            id={`${id}-driver-search`}
            type="search"
            value={driverSearch}
            disabled={busy || !online}
            onChange={(event) => setDriverSearch(event.target.value)}
          />
          <label htmlFor={`${id}-driver`}>Driver</label>
          <select
            id={`${id}-driver`}
            required
            value={selectedDriver}
            disabled={busy || !online}
            onChange={(event) => {
              setDriver(event.target.value);
              setVehicle("");
            }}
          >
            <option value="">Choose active driver</option>
            {drivers.data?.items.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          {(drivers.isError || schedule.isError) && (
            <p role="alert">Drivers could not be loaded. Close and try again.</p>
          )}
          {options.isPending && selectedDriver && <p role="status">Loading usable trucks...</p>}
          {options.isError && (
            <p role="alert">
              Available trucks could not be loaded.{" "}
              <button type="button" onClick={() => void options.refetch()}>
                Try again
              </button>
            </p>
          )}
          {options.data?.items.length === 0 && (
            <p>
              No usable truck right now. <Link to="/fleet">Open Fleet</Link>
            </p>
          )}
          <fieldset disabled={busy || !online}>
            <legend>Usable truck</legend>
            {options.data?.items.map((item) => (
              <div className="swap-option" key={item.vehicleId}>
                <input
                  id={`${id}-${item.vehicleId}`}
                  type="radio"
                  name="vehicle"
                  required
                  checked={vehicle === item.vehicleId}
                  onChange={() => setVehicle(item.vehicleId)}
                />
                <label htmlFor={`${id}-${item.vehicleId}`}>
                  <strong>{item.plate}</strong>
                  <span>{item.belongsTo ? `Belongs to ${item.belongsTo}` : "Unassigned"}</span>
                  {item.runningShiftUserName && item.runningShiftStartAt && (
                    <span>
                      In use by {item.runningShiftUserName} since {companyTime(item.runningShiftStartAt)}
                    </span>
                  )}
                </label>
              </div>
            ))}
          </fieldset>
          <label htmlFor={`${id}-to`}>To (optional)</label>
          <input
            id={`${id}-to`}
            type="date"
            min={today}
            value={to}
            disabled={busy || !online}
            onChange={(event) => setTo(event.target.value)}
          />
          {repair && (
            <p>
              Linked repair: {repair.workshop}, {defectDate(repair.scheduledFor)}. The swap ends when the
              repair is done.
            </p>
          )}
          {error && <p role="alert">{error}</p>}
          <button className="ui-button primary" disabled={busy || !online || !vehicle || !selectedDriver}>
            Save swap
          </button>
        </form>
      )}
    </Drawer>
  );
}
