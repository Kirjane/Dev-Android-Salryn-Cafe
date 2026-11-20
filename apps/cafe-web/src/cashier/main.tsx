import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { WorkstationApp } from "./WorkstationApp";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <WorkstationApp />
  </StrictMode>,
);
