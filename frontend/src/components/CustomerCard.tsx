import type { CustomerSnapshot, DemoCredential } from "../types";

interface Props {
  customer: CustomerSnapshot | null;
  credentials: DemoCredential[];
  onUse: (c: DemoCredential) => void;
  disabled: boolean;
}

const money = (n: number) => n.toLocaleString("en-US", { style: "currency", currency: "USD" });

export default function CustomerCard({ customer, credentials, onUse, disabled }: Props) {
  if (!customer) {
    return (
      <section className="card">
        <h2>Customer</h2>
        <p className="muted">
          Not verified. Account tools refuse to run until the Concierge verifies a phone number and PIN.
        </p>
        <h3>Demo accounts</h3>
        <ul className="creds">
          {credentials.map((c) => (
            <li key={c.phone}>
              <div>
                <strong>{c.name}</strong>
                <span className="mono">
                  {c.phone} · PIN {c.pin}
                </span>
                <span className="muted small">{c.profile}</span>
              </div>
              <button className="chip" disabled={disabled} onClick={() => onUse(c)} title="Send these credentials">
                Use
              </button>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  return (
    <section className="card">
      <h2>
        Customer <span className="verified">verified</span>
      </h2>
      <div className="customer-head">
        <strong>{customer.name}</strong>
        <span className="mono muted">
          {customer.accountId} · {customer.phone}
        </span>
      </div>
      <ul className="lines">
        {customer.lines.map((l) => (
          <li key={l.number}>
            <div>
              <span className="mono">{l.number}</span>
              <span className="muted small">
                {l.user} · {l.plan} · {l.device}
              </span>
            </div>
            <span className={`status ${l.status.startsWith("active") ? "ok" : "bad"}`}>{l.status}</span>
          </li>
        ))}
      </ul>
      <div className="customer-foot">
        {customer.homeInternet && <span>Home: {customer.homeInternet}</span>}
        <span>Balance: {money(customer.balance)}</span>
      </div>
    </section>
  );
}
