import { useEffect, useState, type FormEvent } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Panel, Toggle } from "@fleetops/ui";
import { apiClient, type components } from "@fleetops/api-client";
import { useSession } from "../auth/AuthGate";
import { peopleApi } from "../people/api";

type Prefs = components["schemas"]["NotifyPreferences"];

async function loaded<T>(result: { data?: T; error?: unknown }): Promise<T> {
  if (result.error || result.data === undefined) throw new Error("request_failed");
  return result.data;
}

const prefLabels: [keyof Prefs, string][] = [
  ["criticalPush", "Push for critical alerts"],
  ["criticalEmail", "E-mail for critical alerts"],
  ["digestEmail", "Daily digest e-mail at 07:00"],
];

/** My notifications and Cover (Stories 12.4, 12.5), for every office role. Each user sets only their own. */
export function MyNotifications() {
  const session = useSession();
  const client = useQueryClient();
  const [saveError, setSaveError] = useState("");
  const [coverError, setCoverError] = useState("");
  const [colleague, setColleague] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  useEffect(() => {
    document.title = "My notifications · FleetOps Office";
  }, []);
  const prefs = useQuery({
    queryKey: ["notify-prefs"],
    queryFn: () => apiClient.GET("/api/v1/me/notify-prefs").then(loaded),
  });
  const push = useQuery({
    queryKey: ["push-status"],
    queryFn: () => apiClient.GET("/api/v1/me/push").then(loaded),
  });
  const covers = useQuery({
    queryKey: ["alert-covers"],
    queryFn: () => apiClient.GET("/api/v1/me/alert-covers").then(loaded),
  });
  const colleagues = useQuery({
    queryKey: ["cover-colleagues"],
    queryFn: () => peopleApi.list({ page: 1, pageSize: 200, status: "active" }),
    retry: false,
  });
  const officeColleagues =
    colleagues.data?.items.filter(
      (person) =>
        person.id !== session.data?.employeeId &&
        person.roles.some((role) => role === "dispatcher" || role === "hr" || role === "owner"),
    ) ?? [];
  async function savePref(key: keyof Prefs, value: boolean) {
    if (!prefs.data) return;
    setSaveError("");
    const result = await apiClient.PUT("/api/v1/me/notify-prefs", { body: { ...prefs.data, [key]: value } });
    if (result.error) {
      setSaveError("Your preferences could not be saved. Try again.");
      return;
    }
    await client.invalidateQueries({ queryKey: ["notify-prefs"] });
  }
  async function addCover(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCoverError("");
    if (!colleague || !from || !to) {
      setCoverError("Choose a colleague and both dates.");
      return;
    }
    if (to < from) {
      setCoverError("The end date must be on or after the start date.");
      return;
    }
    const result = await apiClient.POST("/api/v1/me/alert-covers", { body: { toId: colleague, from, to } });
    if (result.error) {
      setCoverError("The cover could not be saved. Check the dates and try again.");
      return;
    }
    setColleague("");
    setFrom("");
    setTo("");
    await client.invalidateQueries({ queryKey: ["alert-covers"] });
  }
  async function removeCover(id: string) {
    setCoverError("");
    const result = await apiClient.DELETE("/api/v1/me/alert-covers/{id}", { params: { path: { id } } });
    if (result.error) {
      setCoverError("The cover could not be removed. Try again.");
      return;
    }
    await client.invalidateQueries({ queryKey: ["alert-covers"] });
  }
  return (
    <div className="page me-page">
      <h1>My notifications</h1>
      <Panel title="How you are told">
        {prefs.isError && <p role="alert">Your preferences could not be loaded.</p>}
        {prefs.data && (
          <div className="me-prefs">
            {prefLabels.map(([key, text]) => (
              <Toggle
                key={key}
                label={text}
                checked={prefs.data[key] ?? true}
                onChange={(value) => void savePref(key, value)}
              />
            ))}
          </div>
        )}
        {saveError && <p role="alert">{saveError}</p>}
        {push.data && !push.data.enabled && (
          <p className="ui-muted">
            Push is not set up on this server yet. You still get critical alerts by e-mail.
          </p>
        )}
      </Panel>
      <Panel title="Cover">
        <p className="ui-muted">
          While you are away, your alerts also go to the colleague you name for those dates.
        </p>
        {covers.data && covers.data.items.length > 0 ? (
          <ul className="me-covers">
            {covers.data.items.map((cover) => (
              <li key={cover.id}>
                {cover.colleague} {"·"} {formatDay(cover.from)} {"–"} {formatDay(cover.to)}{" "}
                <button className="ui-button ghost" type="button" onClick={() => void removeCover(cover.id)}>
                  Remove cover by {cover.colleague}
                </button>
              </li>
            ))}
          </ul>
        ) : (
          covers.data && <p>No cover set.</p>
        )}
        <form className="me-cover-form" aria-label="Add cover" onSubmit={(event) => void addCover(event)}>
          <label htmlFor="cover-colleague">Colleague</label>
          <select
            id="cover-colleague"
            value={colleague}
            onChange={(event) => setColleague(event.target.value)}
          >
            <option value="">Choose a colleague</option>
            {officeColleagues.map((person) => (
              <option key={person.id} value={person.id}>
                {person.name}
              </option>
            ))}
          </select>
          <label htmlFor="cover-from">From</label>
          <input id="cover-from" type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
          <label htmlFor="cover-to">To</label>
          <input id="cover-to" type="date" value={to} onChange={(event) => setTo(event.target.value)} />
          <button className="ui-button primary" type="submit">
            Add cover
          </button>
        </form>
        {coverError && <p role="alert">{coverError}</p>}
      </Panel>
    </div>
  );
}

function formatDay(value: string) {
  const [year, month, day] = value.split("-");
  return `${day}.${month}.${year}`;
}
