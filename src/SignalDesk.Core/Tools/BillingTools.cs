using System.ComponentModel;
using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Tools;

public sealed record BillLine(string Description, decimal Amount, string? Line);

public sealed record BillSummary(
    string Period,
    DateOnly DueDate,
    decimal Total,
    IReadOnlyList<BillLine> Items,
    decimal? PreviousTotal,
    IReadOnlyList<BillLine> NewCharges,
    decimal PastDue,
    decimal TotalDue,
    bool Overdue,
    decimal CreditsApplied,
    Payment? LastPayment);

public sealed record CreditResult(bool Applied, decimal Amount, decimal NewBalance, decimal RemainingAllowance, string Message);

public sealed record ArrangementResult(
    bool Created, decimal DownPayment, int Installments, decimal InstallmentAmount, DateOnly? FirstInstallmentDue,
    IReadOnlyList<string> RestoredLines, string Message);

public sealed class BillingTools(DeskContext ctx)
{
    public const decimal CourtesyCreditLimit = 25m;
    public const decimal MinDownPaymentShare = 0.25m;
    public const int MaxInstallments = 3;

    [Description("Get the customer's current bill with line items, charges that are new versus last month, past-due amounts and the last payment.")]
    public object GetCurrentBill()
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        Bill bill = c.CurrentBill;
        Bill? previous = c.PreviousBill;
        var previousDescriptions = previous?.Items.Select(i => i.Description).ToHashSet() ?? [];
        List<BillLine> newCharges = bill.Items
            .Where(i => !previousDescriptions.Contains(i.Description))
            .Select(ToLine)
            .ToList();

        return new BillSummary(
            bill.Period, bill.DueDate, bill.Total, bill.Items.Select(ToLine).ToList(), previous?.Total, newCharges,
            c.PastDue, c.Balance, bill.DueDate < TelcoData.Today && c.Balance > 0 && c.Arrangement is null,
            c.Credits.Sum(x => x.Amount), c.Payments.LastOrDefault());
    }

    [Description("Get the customer's recent payments, including whether each was late.")]
    public object GetPaymentHistory()
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;
        return c.Payments;
    }

    [Description("Apply a one-time courtesy credit to the account. Policy: at most $25 of courtesy credits per rolling 12 months. Requires approval.")]
    public object ApplyCourtesyCredit(
        [Description("Credit amount in USD")] decimal amount,
        [Description("Short reason shown on the bill, e.g. 'Late fee waiver'")] string reason)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        decimal used = c.Credits.Where(x => x.Date > TelcoData.Today.AddMonths(-12)).Sum(x => x.Amount);
        decimal remaining = CourtesyCreditLimit - used;
        if (amount <= 0)
        {
            return new CreditResult(false, amount, c.Balance, remaining, "Credit amount must be positive.");
        }

        if (amount > remaining)
        {
            ctx.Record("credit_refused", $"${amount:0.00} requested, ${remaining:0.00} allowed");
            return new CreditResult(false, amount, c.Balance, remaining,
                $"Refused: ${amount:0.00} exceeds the courtesy-credit allowance (${remaining:0.00} left of ${CourtesyCreditLimit:0} per 12 months). A supervisor must review larger adjustments.");
        }

        c.Credits.Add(new Credit(TelcoData.Today, amount, reason));
        ctx.Record("credit_applied", $"${amount:0.00} - {reason}");
        return new CreditResult(true, amount, c.Balance, remaining - amount, $"Credit of ${amount:0.00} applied ({reason}).");
    }

    [Description("Set up a payment arrangement for an overdue balance: a down payment today plus equal monthly installments. Restores lines suspended for non-payment. Requires approval.")]
    public object SetPaymentArrangement(
        [Description("Amount the customer pays today, at least 25% of the balance")] decimal downPayment,
        [Description("Number of monthly installments for the remainder (1-3)")] int installments)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        decimal balance = c.Balance;
        if (balance <= 0) return new ArrangementResult(false, 0, 0, 0, null, [], "There is no balance to arrange.");
        if (c.Arrangement is not null) return new ArrangementResult(false, 0, 0, 0, null, [], "An arrangement is already in place.");

        decimal minimum = Math.Round(balance * MinDownPaymentShare, 2);
        if (downPayment < minimum || downPayment > balance)
        {
            return new ArrangementResult(false, downPayment, installments, 0, null, [],
                $"Down payment must be between ${minimum:0.00} and ${balance:0.00}.");
        }

        if (installments is < 1 or > MaxInstallments)
        {
            return new ArrangementResult(false, downPayment, installments, 0, null, [], $"Installments must be between 1 and {MaxInstallments}.");
        }

        decimal installmentAmount = Math.Round((balance - downPayment) / installments, 2);
        var firstDue = TelcoData.Today.AddMonths(1);
        c.Arrangement = new PaymentArrangement(downPayment, installments, installmentAmount, firstDue);
        c.Payments.Add(new Payment(TelcoData.Today, downPayment, "Card on file", false));

        List<string> restored = [];
        foreach (Line line in c.Lines.Where(l => l.Status == LineStatus.Suspended && l.SuspendReason == "non-payment"))
        {
            line.Status = LineStatus.Active;
            line.SuspendReason = null;
            restored.Add(line.Number);
        }

        ctx.Record("payment_arrangement", $"${downPayment:0.00} today + {installments} x ${installmentAmount:0.00}; restored {restored.Count} line(s)");
        return new ArrangementResult(true, downPayment, installments, installmentAmount, firstDue, restored,
            restored.Count > 0 ? "Arrangement created and service restored." : "Arrangement created.");
    }

    private static BillLine ToLine(BillItem i) => new(i.Description, i.Amount, i.Line);
}
