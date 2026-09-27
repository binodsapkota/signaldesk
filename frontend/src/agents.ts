import type { AgentKey } from "./types";

export const AGENT_STYLE: Record<AgentKey, { color: string; initials: string; label: string }> = {
  concierge: { color: "#38bdf8", initials: "CO", label: "Concierge" },
  tech_support: { color: "#fb923c", initials: "TS", label: "Tech Support" },
  billing: { color: "#4ade80", initials: "BI", label: "Billing" },
  account: { color: "#f472b6", initials: "AS", label: "Account Services" },
  plans: { color: "#a78bfa", initials: "PT", label: "Plans & Travel" },
  retention: { color: "#facc15", initials: "LO", label: "Loyalty" },
};

export const AGENT_ORDER: AgentKey[] = ["concierge", "tech_support", "billing", "account", "plans", "retention"];

export const agentLabel = (key: string) => AGENT_STYLE[key as AgentKey]?.label ?? key;

export const agentColor = (key: string) => AGENT_STYLE[key as AgentKey]?.color ?? "#94a3b8";
