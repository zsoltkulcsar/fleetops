import { useEffect, useRef, useState } from "react";

// Browser speech recognition is optional; the note remains editable on every device.
type SpeechInput = {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  onresult:
    | ((event: { results: { [index: number]: { [index: number]: { transcript: string } } } }) => void)
    | null;
  onerror: (() => void) | null;
  onend: (() => void) | null;
  start: () => void;
  abort: () => void;
};
type SpeechBrowser = {
  SpeechRecognition?: new () => SpeechInput;
  webkitSpeechRecognition?: new () => SpeechInput;
};

export function useVoiceNote(onText: (text: string) => void) {
  const browser = window as unknown as SpeechBrowser;
  const constructor = browser.SpeechRecognition ?? browser.webkitSpeechRecognition;
  const recognition = useRef<SpeechInput | undefined>(undefined);
  const [listening, setListening] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => () => recognition.current?.abort(), []);
  const start = () => {
    if (!constructor || listening) return;
    const input = new constructor();
    recognition.current = input;
    input.lang = "en-GB";
    input.continuous = false;
    input.interimResults = false;
    input.onresult = (event) => onText(event.results[0][0].transcript);
    input.onerror = () => {
      setListening(false);
      setError("Voice input could not be used. Type your note or use your keyboard microphone.");
    };
    input.onend = () => setListening(false);
    try {
      input.start();
      setListening(true);
      setError("");
    } catch {
      setError("Voice input could not be started. Type your note.");
    }
  };
  return { available: !!constructor, listening, error, start };
}
