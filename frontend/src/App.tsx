import { useCallback, useEffect, useRef, useState } from "react";
import { createSession, getAgents, getHealth, getScenarios, resolveApproval, sendMessage } from "./api";
import { agentLabel } from "./agents";
import AgentRoster from "./components/AgentRoster";
import ChatPanel from "./components/ChatPanel";
import CustomerCard from "./components/CustomerCard";
import RoutingMap from "./components/RoutingMap";
import TracePanel from "./components/TracePanel";
import type {
  AgentInfo,
  AgentKey,
  AuditEntry,
  ChatItem,
  CustomerSnapshot,
  DemoCredential,
  DeskEvent,
  Health,
  Scenario,
  TraceItem,
} from "./types";

let seq = 0;
const nextId = () => `i${++seq}`;

export default function App() {
  const [health, setHealth] = useState<Health | null>(null);
  const [agents, setAgents] = useState<AgentInfo[]>([]);
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [credentials, setCredentials] = useState<DemoCredential[]>([]);

  const [sessionId, setSessionId] = useState<string | null>(null);
  const [items, setItems] = useState<ChatItem[]>([]);
  const [trace, setTrace] = useState<TraceItem[]>([]);
  const [activeAgent, setActiveAgent] = useState<AgentKey>("concierge");
  const [path, setPath] = useState<AgentKey[]>(["concierge"]);
  const [customer, setCustomer] = useState<CustomerSnapshot | null>(null);
  const [audit, setAudit] = useState<AuditEntry[]>([]);
  const [pendingApproval, setPendingApproval] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [scenario, setScenario] = useState<Scenario | null>(null);
  const [step, setStep] = useState(0);
  const [backendError, setBackendError] = useState<string | null>(null);
  const sessionRef = useRef<string | null>(null);

  useEffect(() => {
    Promise.all([getHealth(), getAgents(), getScenarios()])
      .then(([h, a, s]) => {
        setHealth(h);
        setAgents(a);
        setScenarios(s.scenarios);
        setCredentials(s.credentials);
      })
      .catch(() => setBackendError("Can't reach the SignalDesk API. Start it with `dotnet run --project src/SignalDesk.Api`."));
  }, []);

  const reset = useCallback(() => {
    sessionRef.current = null;
    setSessionId(null);
    setItems([]);
    setTrace([]);
    setActiveAgent("concierge");
    setPath(["concierge"]);
    setCustomer(null);
    setAudit([]);
    setPendingApproval(null);
    setScenario(null);
    setStep(0);
  }, []);

  const onEvent = useCallback((e: DeskEvent) => {
    switch (e.type) {
      case "token":
        setItems((prev) => {
          const last = prev[prev.length - 1];
          if (last?.kind === "agent" && last.streaming && last.agent === e.data.agent) {
            return [...prev.slice(0, -1), { ...last, text: last.text + e.data.text }];
          }
          return [...prev, { kind: "agent", id: nextId(), agent: e.data.agent, text: e.data.text, streaming: true }];
        });
        break;
      case "message":
        setItems((prev) => {
          const idx = prev.findLastIndex((i) => i.kind === "agent" && i.streaming && i.agent === e.data.agent);
          if (idx < 0) return [...prev, { kind: "agent", id: nextId(), agent: e.data.agent, text: e.data.text, streaming: false }];
          const copy = [...prev];
          copy[idx] = { ...(copy[idx] as Extract<ChatItem, { kind: "agent" }>), text: e.data.text, streaming: false };
          return copy;
        });
        break;
      case "tool_call":
        setItems((prev) => [...prev, { kind: "tool", id: nextId(), agent: e.data.agent, tool: e.data.tool, callId: e.data.callId }]);
        setTrace((prev) => [
          ...prev,
          { id: nextId(), kind: "tool", agent: e.data.agent, title: e.data.tool, callId: e.data.callId, args: e.data.args, at: Date.now() },
        ]);
        break;
      case "tool_result":
        setItems((prev) => prev.map((i) => (i.kind === "tool" && i.callId === e.data.callId ? { ...i, ok: e.data.ok } : i)));
        setTrace((prev) => prev.map((t) => (t.callId === e.data.callId ? { ...t, ok: e.data.ok, result: e.data.result } : t)));
        break;
      case "handoff":
        setActiveAgent(e.data.to);
        setPath((p) => [...p, e.data.to]);
        setItems((prev) => [...prev, { kind: "handoff", id: nextId(), from: e.data.from, to: e.data.to, reason: e.data.reason }]);
        setTrace((prev) => [
          ...prev,
          {
            id: nextId(),
            kind: "handoff",
            agent: e.data.to,
            title: `from ${agentLabel(e.data.from)}`,
            detail: e.data.reason ?? undefined,
            at: Date.now(),
          },
        ]);
        break;
      case "approval_required":
        setPendingApproval(e.data.requestId);
        setItems((prev) => [...prev, { kind: "approval", id: nextId(), approval: e.data, status: "pending" }]);
        setTrace((prev) => [
          ...prev,
          { id: nextId(), kind: "approval", agent: e.data.agent, title: e.data.tool, detail: e.data.summary, at: Date.now() },
        ]);
        break;
      case "approval_resolved":
        setPendingApproval(null);
        setItems((prev) =>
          prev.map((i) =>
            i.kind === "approval" && i.approval.requestId === e.data.requestId
              ? { ...i, status: e.data.approved ? "approved" : "declined" }
              : i,
          ),
        );
        break;
      case "turn_end":
        setActiveAgent(e.data.activeAgent);
        setPath(e.data.path);
        setCustomer(e.data.customer);
        setAudit(e.data.audit);
        break;
      case "error":
        setItems((prev) => [...prev, { kind: "error", id: nextId(), text: e.data.message }]);
        setTrace((prev) => [...prev, { id: nextId(), kind: "error", agent: "concierge", title: "Error", detail: e.data.message, at: Date.now() }]);
        break;
    }
  }, []);

  const ensureSession = useCallback(async () => {
    if (sessionRef.current) return sessionRef.current;
    const s = await createSession();
    sessionRef.current = s.id;
    setSessionId(s.id);
    return s.id;
  }, []);

  const run = useCallback(async (work: () => Promise<void>) => {
    setBusy(true);
    try {
      await work();
    } catch (err) {
      setItems((prev) => [...prev, { kind: "error", id: nextId(), text: (err as Error).message }]);
    } finally {
      setBusy(false);
    }
  }, []);

  const send = useCallback(
    (text: string, fromScenario = false) =>
      run(async () => {
        const id = await ensureSession();
        setItems((prev) => [...prev, { kind: "customer", id: nextId(), text }]);
        if (fromScenario) setStep((s) => s + 1);
        await sendMessage(id, text, onEvent);
      }),
    [ensureSession, onEvent, run],
  );

  const decide = useCallback(
    (requestId: string, approved: boolean) =>
      run(async () => {
        if (!sessionRef.current) return;
        await resolveApproval(sessionRef.current, requestId, approved, onEvent);
      }),
    [onEvent, run],
  );

  const startScenario = useCallback(
    (s: Scenario) => {
      reset();
      setScenario(s);
      setTimeout(() => send(s.steps[0], true), 0);
    },
    [reset, send],
  );

  const suggestion = scenario && step < scenario.steps.length ? scenario.steps[step] : null;

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <img src="/favicon.svg" alt="" width={30} height={30} />
          <div>
            <h1>SignalDesk</h1>
            <p>Aurora Mobile · multi-agent customer care</p>
          </div>
        </div>
        <div className="topbar-right">
          {health && (
            <span className={`badge ${health.mode}`} title="Model backing the agents">
              {health.mode === "demo" ? "Demo mode · offline agents" : `Live · ${health.model}`}
            </span>
          )}
          <span className="badge subtle">Microsoft Agent Framework · handoff workflow</span>
          <button className="ghost" onClick={reset} disabled={busy}>
            New conversation
          </button>
        </div>
      </header>

      {backendError && <div className="banner">{backendError}</div>}

      <main className="layout">
        <aside className="left">
          <AgentRoster agents={agents} active={activeAgent} visited={path} busy={busy} />
          <CustomerCard customer={customer} credentials={credentials} onUse={(c) => send(`${c.phone}, PIN ${c.pin}`)} disabled={busy} />
        </aside>

        <ChatPanel
          items={items}
          busy={busy}
          activeAgent={activeAgent}
          pendingApproval={pendingApproval}
          scenarios={scenarios}
          scenario={scenario}
          suggestion={suggestion}
          started={sessionId !== null}
          onSend={(t) => send(t, t === suggestion)}
          onDecide={decide}
          onScenario={startScenario}
        />

        <aside className="right">
          <RoutingMap path={path} active={activeAgent} />
          <TracePanel trace={trace} audit={audit} />
        </aside>
      </main>
    </div>
  );
}
