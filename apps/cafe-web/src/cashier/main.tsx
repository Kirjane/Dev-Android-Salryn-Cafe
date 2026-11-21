import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { LiveCafeWorkstation } from "./LiveCafeWorkstation";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <LiveCafeWorkstation />
  </StrictMode>,
);
