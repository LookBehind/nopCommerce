import type { Agent } from "./types";

// Static roster for P1 (the picker is cosmetic until P2 wires the agent loop).
export const AGENTS: Agent[] = [
  { id: "analyst", name: "Analyst", description: "General BI analyst — queries, charts, summaries." },
  { id: "ops", name: "Operations", description: "Order flow, delivery and vendor operations." },
  { id: "finance", name: "Finance", description: "Revenue, reconciliation and invoicing." },
];

export function agentById(id: string): Agent {
  return AGENTS.find((a) => a.id === id) ?? AGENTS[0];
}
