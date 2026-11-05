import type { ReactNode } from "react";

export function Plate({ children }: { children: ReactNode }) {
  return (
    <span className="plate">
      <span className="eu">EU</span>
      <span className="ui-mono">{children}</span>
    </span>
  );
}
