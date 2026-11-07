import { useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { EmptyState, Panel } from "@fleetops/ui";
import { en } from "../i18n/en";
import { useSession } from "../auth/AuthGate";

type Job = {
  name: string;
  startedAt: string | null;
  finishedAt: string | null;
  status: string | null;
  processed: number;
};
type JobList = { items: Job[] };

async function loadJobs(): Promise<JobList> {
  const response = await fetch("/api/v1/admin/jobs");
  if (!response.ok) throw Object.assign(new Error("jobs_unavailable"), { status: response.status });
  return (await response.json()) as JobList;
}

export function Settings() {
  const session = useSession();
  const client = useQueryClient();
  const jobs = useQuery({
    queryKey: ["admin-jobs"],
    queryFn: loadJobs,
    enabled: session.data?.roles.includes("owner") === true,
    retry: false,
  });
  const jobError = jobs.error as (Error & { status?: number }) | null;
  if (!session.data?.roles.includes("owner") || jobError?.status === 404) return <SettingsPage />;
  return (
    <>
      <SettingsPage />
      {jobs.data && (
        <Panel title={en.settings.jobs.title}>
          <div className="jobs-panel">
            {jobs.data.items.map((job) => (
              <div className="job-row" key={job.name}>
                <span>{job.name}</span>
                <span>{job.status ?? en.settings.jobs.neverRun}</span>
                <span>{job.finishedAt ? new Date(job.finishedAt).toLocaleString() : "—"}</span>
                <button
                  type="button"
                  className="ui-button"
                  onClick={async () => {
                    await fetch(`/api/v1/admin/jobs/${encodeURIComponent(job.name)}/run`, { method: "POST" });
                    await client.invalidateQueries({ queryKey: ["admin-jobs"] });
                  }}
                >
                  {en.settings.jobs.runNow}
                </button>
              </div>
            ))}
          </div>
        </Panel>
      )}
    </>
  );
}

function SettingsPage() {
  useEffect(() => {
    document.title = `${en.nav.settings} · ${en.appName}`;
  }, []);

  return (
    <div className="page">
      <h1>{en.nav.settings}</h1>
      <p className="ui-muted">{en.subtitles.settings}</p>
      <EmptyState heading={en.states.empty}>{en.states.emptyBody}</EmptyState>
    </div>
  );
}
