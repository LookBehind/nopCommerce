import { create } from "zustand";

// ---- Toasts ----

export type ToastKind = "info" | "success" | "error";
export interface Toast {
  id: number;
  kind: ToastKind;
  text: string;
}

interface ToastState {
  toasts: Toast[];
  push: (kind: ToastKind, text: string) => void;
  dismiss: (id: number) => void;
}

let toastSeq = 1;

export const useToasts = create<ToastState>((set) => ({
  toasts: [],
  push: (kind, text) => {
    const id = toastSeq++;
    set((s) => ({ toasts: [...s.toasts, { id, kind, text }] }));
    setTimeout(() => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })), 4200);
  },
  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}));

/** Imperative helper usable outside React components. */
export const toast = {
  info: (t: string) => useToasts.getState().push("info", t),
  success: (t: string) => useToasts.getState().push("success", t),
  error: (t: string) => useToasts.getState().push("error", t),
};

// ---- Prompt / confirm dialogs (styled replacements for window.prompt/confirm) ----

export interface DialogRequest {
  id: number;
  mode: "prompt" | "confirm";
  title: string;
  message?: string;
  defaultValue?: string;
  placeholder?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  resolve: (value: string | boolean | null) => void;
}

interface DialogState {
  current: DialogRequest | null;
  open: (req: Omit<DialogRequest, "id" | "resolve">, resolve: DialogRequest["resolve"]) => void;
  close: () => void;
}

let dialogSeq = 1;

export const useDialog = create<DialogState>((set) => ({
  current: null,
  open: (req, resolve) => set({ current: { ...req, id: dialogSeq++, resolve } }),
  close: () => set({ current: null }),
}));

export function promptDialog(opts: {
  title: string;
  message?: string;
  defaultValue?: string;
  placeholder?: string;
  confirmLabel?: string;
}): Promise<string | null> {
  return new Promise((resolve) => {
    useDialog.getState().open({ mode: "prompt", ...opts }, (v) => resolve(typeof v === "string" ? v : null));
  });
}

export function confirmDialog(opts: {
  title: string;
  message?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
}): Promise<boolean> {
  return new Promise((resolve) => {
    useDialog.getState().open({ mode: "confirm", ...opts }, (v) => resolve(v === true));
  });
}
