import { AGENT_STYLE } from "../agents";
import type { AgentKey } from "../types";

interface Props {
  path: AgentKey[];
  active: AgentKey;
}

const W = 340;
const H = 250;
const CENTER = { x: W / 2, y: 130 };
const SPECIALISTS: AgentKey[] = ["tech_support", "billing", "account", "plans", "retention"];

const POS: Record<AgentKey, { x: number; y: number }> = {
  concierge: CENTER,
  ...Object.fromEntries(
    SPECIALISTS.map((k, i) => {
      const angle = -Math.PI / 2 + (i * 2 * Math.PI) / SPECIALISTS.length;
      return [k, { x: CENTER.x + Math.cos(angle) * 126, y: CENTER.y + Math.sin(angle) * 84 }];
    }),
  ),
} as Record<AgentKey, { x: number; y: number }>;

/** Star topology of possible handoffs, with the route this conversation actually took drawn on top. */
export default function RoutingMap({ path, active }: Props) {
  const hops = path.slice(1).map((to, i) => ({ from: path[i], to, n: i + 1 })).filter((h) => h.from !== h.to);

  return (
    <section className="card map-card">
      <h2>Handoff route</h2>
      <svg viewBox={`0 0 ${W} ${H}`} className="map" role="img" aria-label="Agents and the handoffs taken in this conversation">
        <defs>
          <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
            <path d="M0 0 10 5 0 10z" fill="#e2e8f0" />
          </marker>
        </defs>

        {SPECIALISTS.map((k) => (
          <line key={k} x1={CENTER.x} y1={CENTER.y} x2={POS[k].x} y2={POS[k].y} className="edge" />
        ))}
        {SPECIALISTS.flatMap((a, i) =>
          SPECIALISTS.slice(i + 1).map((b) => <line key={a + b} x1={POS[a].x} y1={POS[a].y} x2={POS[b].x} y2={POS[b].y} className="edge faint" />),
        )}

        {hops.map((h) => {
          const a = POS[h.from];
          const b = POS[h.to];
          const dx = b.x - a.x;
          const dy = b.y - a.y;
          const len = Math.hypot(dx, dy) || 1;
          const r = 24;
          const x1 = a.x + (dx / len) * r;
          const y1 = a.y + (dy / len) * r;
          const x2 = b.x - (dx / len) * (r + 4);
          const y2 = b.y - (dy / len) * (r + 4);
          return (
            <g key={h.n}>
              <line x1={x1} y1={y1} x2={x2} y2={y2} className="hop" style={{ stroke: AGENT_STYLE[h.to].color }} markerEnd="url(#arrow)" />
              <circle cx={(x1 + x2) / 2} cy={(y1 + y2) / 2} r="8" className="hop-badge" />
              <text x={(x1 + x2) / 2} y={(y1 + y2) / 2 + 3.5} className="hop-num">
                {h.n}
              </text>
            </g>
          );
        })}

        {(Object.keys(POS) as AgentKey[]).map((k) => {
          const visited = path.includes(k);
          return (
            <g key={k} className={`node ${k === active ? "active" : ""} ${visited ? "visited" : ""}`}>
              <circle cx={POS[k].x} cy={POS[k].y} r="22" style={{ stroke: AGENT_STYLE[k].color, fill: visited ? AGENT_STYLE[k].color + "33" : "#0b1220" }} />
              <text x={POS[k].x} y={POS[k].y + 4} className="node-initials" style={{ fill: AGENT_STYLE[k].color }}>
                {AGENT_STYLE[k].initials}
              </text>
              <text x={POS[k].x} y={POS[k].y + (POS[k].y >= CENTER.y ? 36 : -28)} className="node-label">
                {AGENT_STYLE[k].label}
              </text>
            </g>
          );
        })}
      </svg>
    </section>
  );
}
