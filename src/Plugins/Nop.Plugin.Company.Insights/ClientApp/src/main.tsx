import React from "react";
import { createRoot } from "react-dom/client";
import "react-grid-layout/css/styles.css";
import "react-resizable/css/styles.css";
import "./styles.css";
import { App } from "./App";

const el = document.getElementById("insights-root");
if (el) {
  createRoot(el).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
}
