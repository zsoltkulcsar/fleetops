export function Skeleton({ width = "100%" }: { width?: string }) {
  return <div className="ui-skeleton" style={{ width }} aria-hidden="true" />;
}
