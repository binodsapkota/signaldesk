import { useEffect, useRef, useState } from "react";
import { AGENT_STYLE, agentColor, agentLabel } from "../agents";
import type { AgentKey, ChatItem, Scenario } from "../types";
import ApprovalCard from "./ApprovalCard";

interface Props {
  items: ChatItem[];
  busy: boolean;
  activeAgent: AgentKey;
  pendingApproval: string | null;
  scenarios: Scenario[];
  scenario: Scenario | null;
  suggestion: string | null;
  started: boolean;
  onSend: (text: string) => void;
  onDecide: (requestId: string, approved: boolean) => void;
  onScenario: (s: Scenario) => void;
}

export default function ChatPanel(props: Props) {
  const { items, busy, activeAgent, pendingApproval, scenarios, scenario, suggestion, started, onSend, onDecide, onScenario } = props;
  const [draft, setDraft] = useState("");
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [items]);

  const blocked = busy || pendingApproval !== null;
  const submit = (text: string) => {
    const t = text.trim();
    if (!t || blocked) return;
    setDraft("");
    onSend(t);
  };

  return (
    <section className="chat">
      <div className="chat-head">
        <div>
          <h2>{scenario ? scenario.title : "Customer conversation"}</h2>
          <span className="muted small">{scenario ? `${scenario.customer} · ${scenario.summary}` : "Pick a scenario or type as the customer."}</span>
        </div>
        <span className="handling" style={{ "--agent": agentColor(activeAgent) } as React.CSSProperties}>
          <span className="dot" /> {agentLabel(activeAgent)} is handling this
        </span>
      </div>

      <div className="messages">
        {!started && items.length === 0 && (
          <div className="welcome">
            <h3>Try a scripted customer journey</h3>
            <p className="muted">
              Each journey hands the live conversation between specialist agents. Account changes pause for your approval.
            </p>
            <div className="scenario-grid">
              {scenarios.map((s) => (
                <button key={s.id} className="scenario" onClick={() => onScenario(s)} disabled={busy}>
                  <strong>{s.title}</strong>
                  <span className="muted small">{s.customer}</span>
                  <span className="scenario-summary">{s.summary}</span>
                  <span className="scenario-agents">
                    {s.agents.map((a, i) => (
                      <span key={i} className="mini-avatar" style={{ background: AGENT_STYLE[a].color }} title={agentLabel(a)}>
                        {AGENT_STYLE[a].initials}
                      </span>
                    ))}
                  </span>
                </button>
              ))}
            </div>
          </div>
        )}

        {items.map((item) => {
          switch (item.kind) {
            case "customer":
              return (
                <div key={item.id} className="msg customer">
                  <div className="bubble">{item.text}</div>
                </div>
              );
            case "agent":
              return (
                <div key={item.id} className="msg agent" style={{ "--agent": agentColor(item.agent) } as React.CSSProperties}>
                  <span className="avatar">{AGENT_STYLE[item.agent].initials}</span>
                  <div className="bubble">
                    <span className="who">{agentLabel(item.agent)}</span>
                    <RichText text={item.text} />
                    {item.streaming && <span className="caret" />}
                  </div>
                </div>
              );
            case "tool":
              return (
                <div key={item.id} className="tool-row" style={{ "--agent": agentColor(item.agent) } as React.CSSProperties}>
                  <span className={`tool-pill ${item.ok === undefined ? "running" : item.ok ? "ok" : "fail"}`}>
                    <code>{item.tool}</code>
                    <span>{item.ok === undefined ? "running" : item.ok ? "done" : "refused"}</span>
                  </span>
                </div>
              );
            case "handoff":
              return (
                <div key={item.id} className="handoff">
                  <span style={{ color: agentColor(item.from) }}>{agentLabel(item.from)}</span>
                  <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden>
                    <path d="M4 12h14m-5-5 5 5-5 5" fill="none" stroke="currentColor" strokeWidth="2" />
                  </svg>
                  <span style={{ color: agentColor(item.to) }}>{agentLabel(item.to)}</span>
                  {item.reason && <em>{item.reason}</em>}
                </div>
              );
            case "approval":
              return <ApprovalCard key={item.id} approval={item.approval} status={item.status} busy={busy} onDecide={onDecide} />;
            case "error":
              return (
                <div key={item.id} className="msg-error">
                  {item.text}
                </div>
              );
          }
        })}
        <div ref={endRef} />
      </div>

      <div className="composer">
        {suggestion && !pendingApproval && (
          <button className="suggestion" disabled={busy} onClick={() => submit(suggestion)}>
            <span>Next in script</span> {suggestion}
          </button>
        )}
        {pendingApproval && <div className="hint">Approve or decline the pending action to continue.</div>}
        <form
          onSubmit={(e) => {
            e.preventDefault();
            submit(draft);
          }}
        >
          <input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            placeholder={blocked ? "Waiting..." : "Type as the customer, e.g. \"My internet keeps dropping\""}
            disabled={blocked}
            maxLength={1000}
          />
          <button type="submit" disabled={blocked || !draft.trim()}>
            Send
          </button>
        </form>
      </div>
    </section>
  );
}

/** Renders agent text with simple bullet and numbered lists. */
function RichText({ text }: { text: string }) {
  const blocks: React.ReactNode[] = [];
  let list: string[] = [];
  const flush = () => {
    if (list.length) blocks.push(<ul key={`l${blocks.length}`}>{list.map((l, i) => <li key={i}>{l}</li>)}</ul>);
    list = [];
  };
  for (const line of text.split("\n")) {
    const m = line.match(/^\s*(?:-|\d+\.)\s+(.*)$/);
    if (m) list.push(m[1]);
    else {
      flush();
      if (line.trim()) blocks.push(<p key={`p${blocks.length}`}>{line}</p>);
    }
  }
  flush();
  return <>{blocks}</>;
}
