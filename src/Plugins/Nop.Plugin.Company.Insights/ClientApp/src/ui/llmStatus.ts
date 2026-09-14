import { create } from "zustand";

export type LlmStatus = "unknown" | "warming" | "ready" | "cold";

interface LlmStatusState {
  status: LlmStatus;
  setStatus: (status: LlmStatus) => void;
}

/** Reflects the analysis model's health across the UI (agent picker dot + chat). */
export const useLlmStatus = create<LlmStatusState>((set) => ({
  status: "unknown",
  setStatus: (status) => set({ status }),
}));
