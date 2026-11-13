import { useEffect, useState } from "react";
import { Check, Copy, MessageCircle, Phone, Send } from "lucide-react";

/** Copy, call and WhatsApp for a phone number, plus the in-app quick message when the caller offers one. */
export function ContactActions({
  name,
  phone,
  onMessage,
}: {
  name: string;
  phone: string;
  onMessage?: () => void;
}) {
  const digits = phone.replace(/[^+\d]/g, "");
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
      <a className="people-contact-pill call" href={`tel:${digits}`} aria-label={`Call ${name}`} title="Call">
        <Phone size={14} aria-hidden />
        Call
      </a>
      <a
        className="people-contact-pill whatsapp"
        href={`https://wa.me/${digits.replace("+", "")}`}
        target="_blank"
        rel="noopener noreferrer"
        aria-label={`WhatsApp ${name} (opens WhatsApp)`}
        title="WhatsApp"
      >
        <MessageCircle size={14} aria-hidden />
        WhatsApp
      </a>
      {onMessage && <MessageButton name={name} onMessage={onMessage} />}
      <span className="visually-hidden" aria-live="polite">
        {copied ? "Copied" : ""}
      </span>
    </span>
  );
}

export function MessageButton({ name, onMessage }: { name: string; onMessage: () => void }) {
  return (
    <button
      type="button"
      className="people-contact-pill message"
      aria-label={`Message ${name} in the app`}
      title="Quick message in the app"
      onClick={onMessage}
    >
      <Send size={14} aria-hidden />
      Message
    </button>
  );
}
