import { useId, useState, type FormEvent } from "react";
import { Send } from "lucide-react";
import { Banner, Drawer } from "@fleetops/ui";

const quickMessages = [
  "Please call the office.",
  "Please upload your missing document.",
  "Please check your shift; a correction is needed.",
  "Your plan for tomorrow has changed.",
];
const maxLength = 500;

/**
 * Quick message to a person. Demo only (decisions Round 8): there is no messaging backend yet,
 * so nothing leaves this screen and the dialog says so. Delivery to the driver app is Epic 15.
 */
export function MessageComposer({ name, onClose }: { name: string; onClose: () => void }) {
  const fieldId = useId();
  const [text, setText] = useState("");
  const [sent, setSent] = useState(false);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (text.trim()) setSent(true);
  };
  return (
    <Drawer open title={`Message to ${name}`} closeLabel="Close" onClose={onClose}>
      <div className="people-message">
        <Banner kind="info" label="Demo">
          Demo only: messages are not delivered to the driver app yet. Delivery comes in Epic 15.
        </Banner>
        {sent ? (
          <>
            <p role="status">Demo message shown on this screen only. Nothing was sent to {name}.</p>
            <blockquote className="people-message-preview">{text.trim()}</blockquote>
            <div className="people-message-actions">
              <button type="button" className="ui-button primary" onClick={onClose}>
                Done
              </button>
              <button
                type="button"
                className="ui-button"
                onClick={() => {
                  setText("");
                  setSent(false);
                }}
              >
                Write another
              </button>
            </div>
          </>
        ) : (
          <form onSubmit={submit}>
            <div className="people-message-quick" role="group" aria-label="Quick messages">
              {quickMessages.map((message) => (
                <button key={message} type="button" className="people-chip" onClick={() => setText(message)}>
                  {message}
                </button>
              ))}
            </div>
            <label htmlFor={`${fieldId}-text`}>Message</label>
            <textarea
              id={`${fieldId}-text`}
              rows={5}
              maxLength={maxLength}
              required
              value={text}
              onChange={(event) => setText(event.target.value)}
            />
            <small className="ui-muted tabular-nums">
              {text.length} / {maxLength}
            </small>
            <div className="people-message-actions">
              <button type="submit" className="ui-button primary" disabled={!text.trim()}>
                <Send size={15} aria-hidden /> Send (demo)
              </button>
              <button type="button" className="ui-button" onClick={onClose}>
                Cancel
              </button>
            </div>
          </form>
        )}
      </div>
    </Drawer>
  );
}
