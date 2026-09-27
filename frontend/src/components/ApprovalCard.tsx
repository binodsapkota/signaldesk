import { agentColor, agentLabel } from "../agents";
import type { ApprovalData } from "../types";

interface Props {
  approval: ApprovalData;
  status: "pending" | "approved" | "declined";
  busy: boolean;
  onDecide: (requestId: string, approved: boolean) => void;
}

export default function ApprovalCard({ approval, status, busy, onDecide }: Props) {
  return (
    <div className={`approval ${status}`} style={{ "--agent": agentColor(approval.agent) } as React.CSSProperties}>
      <div className="approval-head">
        <span className="lock" aria-hidden>
          <svg viewBox="0 0 24 24" width="16" height="16">
            <path d="M7 11V8a5 5 0 0 1 10 0v3" fill="none" stroke="currentColor" strokeWidth="2" />
            <rect x="5" y="11" width="14" height="10" rx="2" fill="currentColor" />
          </svg>
        </span>
        <div>
          <strong>Confirmation required</strong>
          <span className="muted small">
            {agentLabel(approval.agent)} wants to run <code>{approval.tool}</code>
          </span>
        </div>
      </div>
      <p className="approval-summary">{approval.summary}</p>
      {Object.keys(approval.args).length > 0 && (
        <dl className="args">
          {Object.entries(approval.args).map(([k, v]) => (
            <div key={k}>
              <dt>{k}</dt>
              <dd>{v === null ? "default" : String(v)}</dd>
            </div>
          ))}
        </dl>
      )}
      {status === "pending" ? (
        <div className="approval-actions">
          <button className="approve" disabled={busy} onClick={() => onDecide(approval.requestId, true)}>
            Approve
          </button>
          <button className="decline" disabled={busy} onClick={() => onDecide(approval.requestId, false)}>
            Decline
          </button>
        </div>
      ) : (
        <div className={`approval-result ${status}`}>{status === "approved" ? "Approved, action executed" : "Declined, nothing changed"}</div>
      )}
    </div>
  );
}
