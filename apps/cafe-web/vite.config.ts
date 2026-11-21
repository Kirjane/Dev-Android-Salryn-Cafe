import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";

export default defineConfig({
  base: "/cafe/",
  plugins: [react()],
  resolve: {
    dedupe: ["react", "react-dom", "lucide-react"],
    alias: {
      "@salryn/cafe-ui": path.resolve(__dirname, "../../packages/salryn-cafe-ui/src/index.ts"),
      "@salryn-brand": path.resolve(__dirname, "../../assets/brand"),
      react: path.resolve(__dirname, "node_modules/react"),
      "react-dom": path.resolve(__dirname, "node_modules/react-dom"),
      "lucide-react": path.resolve(__dirname, "node_modules/lucide-react"),
    },
  },
  server: {
    fs: {
      allow: [path.resolve(__dirname, "../..")],
    },
  },
});
