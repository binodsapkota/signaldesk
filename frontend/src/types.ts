export type AgentKey = "concierge" | "tech_support" | "billing" | "account" | "plans" | "retention";

export interface ToolInfo {
  name: string;
  description: string;
  requiresApproval: boolean;
}

export interface AgentInfo {
  key: AgentKey;
  displayName: string;
  role: string;
  scope: string;
  tools: ToolInfo[];
}

export interface Scenario {
  id: string;
  title: string;
  customer: string;
  summary: string;
  agents: AgentKey[];
  steps: string[];
}

export interface DemoCredential {
  name: string;
  phone: string;
  pin: string;
  profile: string;
}

export interface Health {
  status: string;
  mode: "demo" | "openai";
  model: string;
}

export interface LineInfo {
  number: string;
  user: string;
  relationship: string;
  plan: string;
  device: string;
  status: string;
}

export interface CustomerSnapshot {
  name: string;
  accountId: string;
  phone: string;
  lines: LineInfo[];
  homeInternet: string | null;
  balance: number;
}

export interface AuditEntry {
  at: string;
  action: string;
  detail: string;
}

export interface ApprovalData {
  requestId: string;
  agent: AgentKey;
  tool: string;
  args: Record<string, unknown>;
  summary: string;
}

export interface SessionState {
  id: string;
  mode: string;
  activeAgent: AgentKey;
  path: AgentKey[];
  customer: CustomerSnapshot | null;
  pendingApproval: ApprovalData | null;
}

export type DeskEvent =
  | { type: "token"; data: { agent: AgentKey; text: string } }
  | { type: "message"; data: { agent: AgentKey; text: string } }
  | { type: "handoff"; data: { from: AgentKey; to: AgentKey; reason: string | null } }
  | { type: "tool_call"; data: { agent: AgentKey; tool: string; callId: string; args: Record<string, unknown> } }
  | { type: "tool_result"; data: { agent: AgentKey; tool: string; callId: string; ok: boolean; result: unknown } }
  | { type: "approval_required"; data: ApprovalData }
  | { type: "approval_resolved"; data: { requestId: string; approved: boolean } }
  | {
      type: "turn_end";
      data: {
        activeAgent: AgentKey;
        awaiting: "customer" | "approval";
        path: AgentKey[];
        customer: CustomerSnapshot | null;
        audit: AuditEntry[];
      };
    }
  | { type: "error"; data: { message: string } };

export type ChatItem =
  | { kind: "customer"; id: string; text: string }
  | { kind: "agent"; id: string; agent: AgentKey; text: string; streaming: boolean }
  | { kind: "tool"; id: string; agent: AgentKey; tool: string; callId: string; ok?: boolean }
  | { kind: "handoff"; id: string; from: AgentKey; to: AgentKey; reason: string | null }
  | { kind: "approval"; id: string; approval: ApprovalData; status: "pending" | "approved" | "declined" }
  | { kind: "error"; id: string; text: string };

export interface TraceItem {
  id: string;
  kind: "tool" | "handoff" | "approval" | "error";
  agent: AgentKey;
  title: string;
  detail?: string;
  callId?: string;
  args?: Record<string, unknown>;
  result?: unknown;
  ok?: boolean;
  at: number;
}
