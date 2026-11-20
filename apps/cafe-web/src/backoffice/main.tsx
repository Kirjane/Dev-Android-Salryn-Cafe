import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BackofficeConsoleApp } from "./BackofficeConsoleApp";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BackofficeConsoleApp />
  </StrictMode>,
);
