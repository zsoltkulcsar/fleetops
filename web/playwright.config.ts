import { defineConfig, devices } from "@playwright/test";

declare const process: { env: Record<string, string | undefined> };

export default defineConfig({
  testDir: "e2e",
  use: { baseURL: "http://127.0.0.1:4173", trace: "retain-on-failure" },
  webServer: [
    {
      command: "npm run dev --workspace @fleetops/office -- --strictPort",
      port: 4173,
      reuseExistingServer: !process.env.CI,
    },
    {
      command:
        "npm run build && npm run preview --workspace @fleetops/office -- --host 127.0.0.1 --port 4174 --strictPort",
      port: 4174,
      reuseExistingServer: !process.env.CI,
    },
  ],
  projects: [
    {
      name: "desktop",
      testIgnore: /production\.spec\.ts/,
      use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 900 } },
    },
    {
      name: "tablet",
      testIgnore: /production\.spec\.ts/,
      use: { ...devices["Desktop Chrome"], viewport: { width: 1000, height: 800 } },
    },
    {
      name: "mobile",
      testIgnore: /production\.spec\.ts/,
      use: { ...devices["Desktop Chrome"], viewport: { width: 390, height: 844 } },
    },
    {
      name: "production",
      testMatch: /production\.spec\.ts/,
      use: {
        ...devices["Desktop Chrome"],
        baseURL: "http://127.0.0.1:4174",
        viewport: { width: 1280, height: 900 },
      },
    },
  ],
});
