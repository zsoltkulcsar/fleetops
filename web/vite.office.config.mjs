import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default {
  plugins: [react(), tailwindcss()],
  build: { emptyOutDir: false },
};
