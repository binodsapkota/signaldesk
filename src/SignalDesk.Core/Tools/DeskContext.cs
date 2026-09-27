using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Tools;

public sealed record AuditEntry(DateTimeOffset At, string Action, string Detail);

/// <summary>Result returned by any tool that refuses to run. Agents see the message and must relay it.</summary>
public sealed record ToolError(string Error);

/// <summary>
/// Per-conversation state shared by every agent's tools: the sandboxed data, the verified identity
/// and an audit trail of account changes. Tools read identity from here, never from model arguments.
/// </summary>
public sealed class DeskContext(TelcoData data)
{
    public const int MaxVerificationAttempts = 3;

    public TelcoData Data { get; } = data;
    public Customer? Customer { get; private set; }
    public int FailedVerifications { get; private set; }
    public bool IsLocked => FailedVerifications >= MaxVerificationAttempts;
    public List<AuditEntry> Audit { get; } = [];

    internal void MarkVerified(Customer customer) => Customer = customer;

    internal void RecordFailedVerification() => FailedVerifications++;

    internal void Record(string action, string detail) => Audit.Add(new AuditEntry(DateTimeOffset.UtcNow, action, detail));

    internal bool TryGetCustomer(out Customer customer, out ToolError error)
    {
        customer = Customer!;
        error = new ToolError("Customer is not verified. Ask the Concierge to verify identity (phone number + account PIN) first.");
        return Customer is not null;
    }

    internal Line? ResolveLine(Customer customer, string? lineRef)
    {
        if (string.IsNullOrWhiteSpace(lineRef))
        {
            return customer.Lines[0];
        }

        string digits = TelcoData.Digits(lineRef);
        if (digits.Length >= 4)
        {
            return customer.Lines.FirstOrDefault(l => TelcoData.Digits(l.Number).EndsWith(digits[^Math.Min(digits.Length, 10)..], StringComparison.Ordinal))
                ?? customer.Lines.FirstOrDefault(l => l.Last4 == digits[^4..]);
        }

        return customer.Lines.FirstOrDefault(l =>
            l.UserName.Contains(lineRef, StringComparison.OrdinalIgnoreCase) ||
            l.Relationship.Equals(lineRef, StringComparison.OrdinalIgnoreCase));
    }
}
