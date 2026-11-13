import { useEffect, useState } from "react";
import { Check, Copy, MessageCircle, Phone, Send } from "lucide-react";

/**
 * Phone number with its copy button, then call, WhatsApp and (when the caller offers it) the in-app
 * quick message as icon buttons. Each icon names its action on hover and to assistive technology.
 */
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
      <span className="people-phone">
        <span className="ui-mono">{phone}</span>
        <button
          type="button"
          className="people-icon-button small"
          aria-label={copied ? `Phone number of ${name} copied` : `Copy phone number of ${name}`}
          title={copied ? "Copied" : "Copy number"}
          onClick={() => {
            void navigator.clipboard?.writeText(phone).then(
              () => setCopied(true),
              () => setCopied(false),
            );
          }}
        >
          {copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
        </button>
      </span>
      <span className="people-contact-icons">
        <a className="people-contact-icon call" href={`tel:${digits}`} aria-label={`Call ${name}`} title="Call">
          <Phone size={15} aria-hidden />
        </a>
        <a
          className="people-contact-icon whatsapp"
          href={`https://wa.me/${digits.replace("+", "")}`}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={`WhatsApp ${name} (opens WhatsApp)`}
          title="WhatsApp"
        >
          <MessageCircle size={15} aria-hidden />
        </a>
        {onMessage && <MessageButton name={name} onMessage={onMessage} />}
      </span>
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
      className="people-contact-icon message"
      aria-label={`Message ${name} in the app`}
      title="Message in the app"
      onClick={onMessage}
    >
      <Send size={15} aria-hidden />
    </button>
  );
}
