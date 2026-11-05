import { CheckCircle2, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { createPortal } from "react-dom";

export function Toast({
  children,
  onClose,
  undo,
  open = true,
  dismissLabel,
  undoLabel,
}: {
  children: ReactNode;
  onClose: () => void;
  undo?: () => void;
  open?: boolean;
  dismissLabel: string;
  undoLabel: string;
}) {
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const paused = hovered || focused;
  const remaining = useRef(10000);
  const started = useRef(Date.now());
  const closeRef = useRef(onClose);
  const wasOpen = useRef(false);
  closeRef.current = onClose;
  useEffect(() => {
    if (open && !wasOpen.current) {
      remaining.current = 10000;
      started.current = Date.now();
    }
    wasOpen.current = open;
    if (!open) {
      setHovered(false);
      setFocused(false);
      return;
    }
  }, [open]);
  useEffect(() => {
    if (paused || !open) return;
    started.current = Date.now();
    const timer = window.setTimeout(() => closeRef.current(), remaining.current);
    return () => {
      remaining.current = Math.max(0, remaining.current - (Date.now() - started.current));
      window.clearTimeout(timer);
    };
  }, [paused, open]);
  const liveText = open ? children : null;
  if (typeof document === "undefined") return null;
  return createPortal(
    <>
      <div
        className="toast-live-region visually-hidden"
        data-testid="toast-live"
        role="status"
        aria-live="polite"
      >
        {liveText}
      </div>
      {open && (
        <div
          className="toast"
          data-testid="toast"
          tabIndex={0}
          onMouseEnter={() => setHovered(true)}
          onMouseLeave={() => setHovered(false)}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
        >
          <CheckCircle2 size={18} aria-hidden />
          <span>{children}</span>
          {undo && (
            <button
              type="button"
              className="ui-button ghost"
              onClick={() => {
                setHovered(false);
                setFocused(false);
                undo();
              }}
            >
              {undoLabel}
            </button>
          )}
          <button
            type="button"
            className="ui-button ghost"
            aria-label={dismissLabel}
            onClick={() => {
              setHovered(false);
              setFocused(false);
              onClose();
            }}
          >
            <X size={16} aria-hidden />
          </button>
        </div>
      )}
    </>,
    document.body,
  );
}
