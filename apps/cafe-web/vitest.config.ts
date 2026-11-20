import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "node:path";

export default defineConfig({
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
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/cashier/testSetup.ts", "./src/backoffice/testSetup.ts"],
    css: true,
  },
});
