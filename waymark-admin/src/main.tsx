import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import Board from "./Board";
import "./index.css";

// The surface flag of the README: this build is Local Admin, on the shop's LAN, and it is the
// one that may show a customer's name. Cloud Admin is the same application built with this set
// to "cloud", where those screens are absent rather than disabled. Nothing in Phase 0.5 shows a
// customer at all, so nothing reads it yet — it is here so the first screen that would has to
// ask the question.
export const SURFACE = "local" as const;

// dir follows the language. Arabic is the same components with dir="rtl"; the layout is written
// in logical properties, so there is nothing else to change (CLAUDE.md §9).
document.documentElement.dir = document.documentElement.lang === "ar" ? "rtl" : "ltr";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Board />
  </StrictMode>,
);
