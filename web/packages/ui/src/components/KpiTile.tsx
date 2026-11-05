export function KpiTile({
  label,
  value,
  detail,
  onClick,
}: {
  label: string;
  value: string;
  detail?: string;
  onClick?: () => void;
}) {
  const content = (
    <>
      <span className="eyebrow">{label}</span>
      <strong>{value}</strong>
      {detail && <span className="ui-muted">{detail}</span>}
    </>
  );
  return onClick ? (
    <button type="button" className="ui-panel kpi-tile" onClick={onClick}>
      {content}
    </button>
  ) : (
    <div className="ui-panel kpi-tile">{content}</div>
  );
}
