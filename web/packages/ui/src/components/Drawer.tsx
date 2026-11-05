import { X } from "lucide-react";
import { useEffect, useRef } from "react";
import type { ReactNode } from "react";
import { createPortal } from "react-dom";

const focusable =
  'button:not([disabled]),a[href],input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"]):not([aria-hidden="true"])';

export function Drawer({
  open,
  title,
  children,
  onClose,
  closeLabel,
}: {
  open: boolean;
  title: string;
  children: ReactNode;
  onClose: () => void;
  closeLabel: string;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  const drawer = useRef<HTMLElement>(null);
  const returnFocus = useRef<HTMLElement | null>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEffect(() => {
    if (!open) return;
    returnFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const appRoot = document.getElementById("app");
    if (!appRoot) throw new Error("Drawer requires an #app root");
    appRoot.inert = true;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const frame = requestAnimationFrame(() => heading.current?.focus());
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        closeRef.current();
        return;
      }
      if (event.key !== "Tab") return;
      const items = drawer.current
        ? [...drawer.current.querySelectorAll<HTMLElement>(focusable)].filter(
            (item) => item.offsetParent !== null,
          )
        : [];
      if (!items.length) {
        event.preventDefault();
        return;
      }
      const first = items[0];
      const last = items[items.length - 1];
      if (
        event.shiftKey &&
        (document.activeElement === first || document.activeElement === heading.current)
      ) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => {
      cancelAnimationFrame(frame);
      document.removeEventListener("keydown", onKeyDown);
      appRoot.inert = false;
      document.body.style.overflow = previousOverflow;
      returnFocus.current?.focus();
      returnFocus.current = null;
    };
  }, [open]);
  if (!open) return null;
  return createPortal(
    <div className="drawer-backdrop" onMouseDown={() => closeRef.current()}>
      <aside
        ref={drawer}
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby="drawer-title"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header>
          <h2 ref={heading} id="drawer-title" tabIndex={-1}>
            {title}
          </h2>
          <button
            type="button"
            className="ui-button ghost"
            aria-label={closeLabel}
            onClick={() => closeRef.current()}
          >
            <X aria-hidden />
          </button>
        </header>
        {children}
      </aside>
    </div>,
    document.body,
  );
}
