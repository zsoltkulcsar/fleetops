import { useEffect, useState } from "react";
import { Check, Copy, Phone } from "lucide-react";

export function ContactActions({ name, phone }: { name: string; phone: string }) {
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(timer);
  }, [copied]);
  return (
    <span className="people-contact-actions">
      <button
        type="button"
        className="people-icon-button"
        aria-label={copied ? `Phone number of ${name} copied` : `Copy phone number of ${name}`}
        title={copied ? "Copied" : "Copy number"}
        onClick={() => {
          void navigator.clipboard?.writeText(phone).then(
            () => setCopied(true),
            () => setCopied(false),
          );
        }}
      >
        {copied ? <Check size={15} aria-hidden /> : <Copy size={15} aria-hidden />}
      </button>
      <a
        className="people-icon-button call"
        href={`tel:${phone.replace(/[^+\d]/g, "")}`}
        aria-label={`Call ${name}`}
        title="Call"
      >
        <Phone size={15} aria-hidden />
      </a>
      <span className="visually-hidden" aria-live="polite">
        {copied ? "Copied" : ""}
      </span>
    </span>
  );
}
