import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { registerSW } from "virtual:pwa-register";
import { App } from "./App";
import "@fleetops/ui/src/tokens.css";
import "./driver.css";

const client = new QueryClient({
  defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
});
registerSW({ immediate: true });
createRoot(document.getElementById("app")!).render(
  <QueryClientProvider client={client}>
    <BrowserRouter basename="/driver">
      <App />
    </BrowserRouter>
  </QueryClientProvider>,
);
