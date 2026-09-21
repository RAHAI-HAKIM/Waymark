import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // StoreServer is the only thing this app talks to, and it is a different origin during
    // development. Proxying rather than enabling CORS on StoreServer: the store's server has
    // no business accepting cross-origin calls just so a dev server can be convenient.
    proxy: {
      "/api": { target: "http://localhost:5290", changeOrigin: true },
    },
  },
});
