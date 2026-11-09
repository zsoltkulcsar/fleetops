import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { VitePWA } from "vite-plugin-pwa";

export default defineConfig({
  base: "/driver/",
  plugins: [
    react(),
    VitePWA({
      registerType: "autoUpdate",
      scope: "/driver/",
      includeAssets: ["favicon.svg", "icon-192.png", "icon-512.png"],
      manifest: {
        name: "FleetOps Driver",
        short_name: "FleetOps",
        start_url: "/driver/",
        scope: "/driver/",
        display: "standalone",
        background_color: "#0e141b",
        theme_color: "#0e141b",
        icons: [
          { src: "/driver/icon-192.png", sizes: "192x192", type: "image/png" },
          { src: "/driver/icon-512.png", sizes: "512x512", type: "image/png" },
        ],
      },
      workbox: {
        globPatterns: ["**/*.{js,css,html,woff2,png,svg,webmanifest}"],
        globIgnores: ["**/api/**"],
        navigateFallback: "/driver/index.html",
        navigateFallbackAllowlist: [/^\/driver\/(?:shift|week|defect|me)?\/?$/],
        navigateFallbackDenylist: [/\/api(?:\/|$)/],
        runtimeCaching: [],
        cleanupOutdatedCaches: true,
      },
    }),
  ],
  build: { emptyOutDir: false },
});
