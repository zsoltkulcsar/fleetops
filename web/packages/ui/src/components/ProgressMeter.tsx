export function ProgressMeter({
  value,
  label,
  ariaLabel,
}: {
  value: number;
  label: string;
  ariaLabel?: string;
}) {
  const safeValue = Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
  return (
    <div className="progress-wrap">
      <div className="progress-label">
        <span>{label}</span>
        <strong className="tabular-nums">{safeValue}%</strong>
      </div>
      <progress aria-label={ariaLabel ?? label} value={safeValue} max="100">
        {safeValue}%
      </progress>
    </div>
  );
}
