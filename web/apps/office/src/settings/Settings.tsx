import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { EmptyState, Panel } from "@fleetops/ui";
import { apiClient, type components } from "@fleetops/api-client";
import { en } from "../i18n/en";
import { useSession } from "../auth/AuthGate";
import { formatBerlinDateTime } from "../shell/date";

async function loadJobs() {
  const result = await apiClient.GET("/api/v1/admin/jobs");
  if (result.error || !result.data) throw new Error("jobs_unavailable");
  return result.data;
}

export function Settings() {
  const session = useSession();
  const client = useQueryClient();
  const [runResults, setRunResults] = useState<
    Record<string, components["schemas"]["JobRunResult"] | "failed">
  >({});
  const isOwner = session.data?.roles.includes("owner") === true;
  const jobs = useQuery({
    queryKey: ["admin-jobs"],
    queryFn: loadJobs,
    enabled: isOwner,
    retry: false,
  });
  if (!isOwner) return <SettingsPage />;
  return (
    <>
      <SettingsPage showEmptyState={!jobs.data} />
      {jobs.data && (
        <Panel title={en.settings.jobs.title}>
          <div className="jobs-panel">
            {jobs.data.items.map((job) => (
              <JobRow
                key={job.name}
                job={job}
                runResult={runResults[job.name]}
                onRun={async () => {
                  const result = await apiClient.POST("/api/v1/admin/jobs/{name}/run", {
                    params: { path: { name: job.name } },
                    headers: { "X-FleetOps-Request": "1" },
                  });
                  if (result.error || !result.data) {
                    setRunResults((current) => ({ ...current, [job.name]: "failed" }));
                    return;
                  }
                  setRunResults((current) => ({ ...current, [job.name]: result.data }));
                  await client.invalidateQueries({ queryKey: ["admin-jobs"] });
                }}
              />
            ))}
          </div>
        </Panel>
      )}
    </>
  );
}

function JobRow({
  job,
  runResult,
  onRun,
}: {
  job: components["schemas"]["JobListItem"];
  runResult: components["schemas"]["JobRunResult"] | "failed" | undefined;
  onRun: () => Promise<void>;
}) {
  return (
    <div className="job-row">
      <span>{job.name}</span>
      <span>{job.status ?? en.settings.jobs.neverRun}</span>
      <span>{formatBerlinDateTime(job.finishedAt)}</span>
      {runResult === "failed" && <span>{en.settings.jobs.runFailed}</span>}
      {runResult && runResult !== "failed" && (
        <span>
          {en.settings.jobs.runResult
            .replace("{status}", runResult.status)
            .replace("{processed}", String(runResult.processed))}
        </span>
      )}
      <button type="button" className="ui-button" onClick={onRun}>
        {en.settings.jobs.runNow}
      </button>
    </div>
  );
}

function SettingsPage({ showEmptyState = true }: { showEmptyState?: boolean }) {
  useEffect(() => {
    document.title = `${en.nav.settings} · ${en.appName}`;
  }, []);

  return (
    <div className="page">
      <h1>{en.nav.settings}</h1>
      <p className="ui-muted">{en.subtitles.settings}</p>
      {showEmptyState && <EmptyState heading={en.states.empty}>{en.states.emptyBody}</EmptyState>}
    </div>
  );
}
