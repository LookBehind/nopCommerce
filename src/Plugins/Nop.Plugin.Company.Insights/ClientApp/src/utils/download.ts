import type { Dataset } from "../types";

function triggerDownload(href: string, filename: string, revoke: boolean) {
  const a = document.createElement("a");
  a.href = href;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  if (revoke) setTimeout(() => URL.revokeObjectURL(href), 1000);
}

function safeName(name: string): string {
  return (name || "export").replace(/[^\w.-]+/g, "_").slice(0, 60);
}

function csvCell(v: unknown): string {
  if (v === null || v === undefined) return "";
  const s = String(v);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

export function downloadCsv(title: string, data: Dataset) {
  const header = data.columns.map((c) => csvCell(c.name)).join(",");
  const lines = data.rows.map((row) => data.columns.map((c) => csvCell(row[c.name])).join(","));
  const csv = [header, ...lines].join("\n");
  const blob = new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8" });
  triggerDownload(URL.createObjectURL(blob), `${safeName(title)}.csv`, true);
}

export function downloadPng(title: string, dataUrl: string) {
  triggerDownload(dataUrl, `${safeName(title)}.png`, false);
}
