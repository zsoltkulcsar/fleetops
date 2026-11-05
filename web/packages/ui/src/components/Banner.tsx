import { AlertCircle, WifiOff } from "lucide-react";
import type { ReactNode } from "react";

export function Banner({
  kind = "error",
  children,
  onRetry,
  label,
  retryLabel,
}: {
  kind?: "error" | "offline" | "info";
  children: ReactNode;
  onRetry?: () => void;
  label: string;
  retryLabel?: string;
}) {
  const Icon = kind === "offline" ? WifiOff : AlertCircle;
  return (
    <div
      className={`banner banner-${kind}`}
      role={kind === "error" ? "alert" : kind === "offline" ? "status" : "region"}
      aria-label={label}
    >
      <Icon size={18} aria-hidden />
      <span>{children}</span>
      {onRetry && (
        <button type="button" className="ui-button" onClick={onRetry}>
          {retryLabel}
        </button>
      )}
    </div>
  );
}
