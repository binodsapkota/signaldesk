import { useState } from "react";
import { agentColor, agentLabel } from "../agents";
import type { AuditEntry, TraceItem } from "../types";

interface Props {
  trace: TraceItem[];
  audit: AuditEntry[];
}

export default function TracePanel({ trace, audit }: Props) {
  const [tab, setTab] = useState<"trace" | "audit">("trace");
  const changes = audit.filter((a) => a.action !== "verified");

  return (
    <section className="card trace-card">
      <div className="tabs">
        <button className={tab === "trace" ? "on" : ""} onClick={() => setTab("trace")}>
          Live trace <span className="count">{trace.length}</span>
        </button>
        <button className={tab === "audit" ? "on" : ""} onClick={() => setTab("audit")}>
          Account changes <span className="count">{changes.length}</span>
        </button>
      </div>

      {tab === "trace" ? (
        <ol className="trace">
          {trace.length === 0 && <li className="muted small empty">Tool calls, handoffs and approvals appear here as agents work.</li>}
          {trace.map((t) => (
            <TraceRow key={t.id} item={t} />
          ))}
        </ol>
      ) : (
        <ol className="audit">
          {changes.length === 0 && <li className="muted small empty">No account changes yet.</li>}
          {changes.map((a, i) => (
            <li key={i}>
              <code>{a.action}</code>
              <span>{a.detail}</span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function TraceRow({ item }: { item: TraceItem }) {
  const [open, setOpen] = useState(false);
  const expandable = item.kind === "tool";
  const state = item.kind !== "tool" ? item.kind : item.ok === undefined ? "running" : item.ok ? "ok" : "fail";

  return (
    <li className={`trace-row st-${state}`} style={{ "--agent": agentColor(item.agent) } as React.CSSProperties}>
      <button className="trace-line" onClick={() => expandable && setOpen((o) => !o)} disabled={!expandable}>
        <span className="trace-dot" />
        <span className="trace-agent">{agentLabel(item.agent)}</span>
        <span className="trace-title">{item.kind === "tool" || item.kind === "approval" ? <code>{item.title}</code> : item.title}</span>
        <span className={`trace-state st-${state}`}>{label(state)}</span>
      </button>
      {item.detail && <p className="trace-detail">{item.detail}</p>}
      {open && (
        <div className="trace-json">
          <div>
            <span>args</span>
            <pre>{JSON.stringify(item.args ?? {}, null, 2)}</pre>
          </div>
          {item.result !== undefined && (
            <div>
              <span>result</span>
              <pre>{JSON.stringify(item.result, null, 2)}</pre>
            </div>
          )}
        </div>
      )}
    </li>
  );
}

function label(state: string) {
  switch (state) {
    case "running":
      return "running";
    case "ok":
      return "ok";
    case "fail":
      return "refused";
    case "handoff":
      return "handoff";
    case "approval":
      return "gated";
    default:
      return "error";
  }
}
