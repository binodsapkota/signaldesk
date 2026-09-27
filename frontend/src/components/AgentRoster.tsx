import { AGENT_ORDER, AGENT_STYLE } from "../agents";
import type { AgentInfo, AgentKey } from "../types";

interface Props {
  agents: AgentInfo[];
  active: AgentKey;
  visited: AgentKey[];
  busy: boolean;
}

export default function AgentRoster({ agents, active, visited, busy }: Props) {
  const byKey = new Map(agents.map((a) => [a.key, a]));

  return (
    <section className="card">
      <h2>Agent team</h2>
      <ul className="roster">
        {AGENT_ORDER.map((key) => {
          const agent = byKey.get(key);
          const style = AGENT_STYLE[key];
          const isActive = key === active;
          const approvals = agent?.tools.filter((t) => t.requiresApproval).length ?? 0;
          return (
            <li
              key={key}
              className={`roster-item ${isActive ? "active" : ""} ${visited.includes(key) ? "visited" : ""}`}
              style={{ "--agent": style.color } as React.CSSProperties}
              title={agent ? `${agent.scope}\n\nTools: ${agent.tools.map((t) => t.name + (t.requiresApproval ? " (approval)" : "")).join(", ")}` : ""}
            >
              <span className="avatar">{style.initials}</span>
              <div className="roster-text">
                <strong>{style.label}</strong>
                <span>
                  {agent?.tools.length ?? 0} tools{approvals > 0 ? ` · ${approvals} need approval` : ""}
                </span>
              </div>
              {isActive && <span className={`live ${busy ? "pulse" : ""}`}>live</span>}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
