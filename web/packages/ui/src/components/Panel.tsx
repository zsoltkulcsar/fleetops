import type { ReactNode } from "react";

export function Panel({
  title,
  children,
  actions,
  filters,
  state = "ready",
}: {
  title: string;
  children: ReactNode;
  actions?: ReactNode;
  filters?: ReactNode;
  state?: "loading" | "error" | "empty" | "offline" | "denied" | "ready";
}) {
  return (
    <section className="ui-panel" data-state={state}>
      <header className="panel-header">
        <h2>{title}</h2>
        {filters ?? actions}
      </header>
      {children}
    </section>
  );
}
