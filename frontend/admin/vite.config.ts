import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig(({ command, isPreview }) => ({
  base: command === "build" || isPreview ? "/admin/" : "/",
  plugins: [react()],
  server: {
    port: 5174,
    strictPort: true,
    proxy: {
      "/api": { target: process.env.SHOPFORGE_API_TARGET ?? "http://localhost:5080", changeOrigin: false },
    },
  },
}));
