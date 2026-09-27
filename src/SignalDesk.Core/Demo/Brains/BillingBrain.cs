using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;

namespace SignalDesk.Core.Demo.Brains;

internal sealed class BillingBrain() : DemoBrain(AgentRoster.Billing)
{
    protected override IReadOnlySet<string> OwnTools { get; } = new HashSet<string>
    {
        "get_current_bill", "get_payment_history", "apply_courtesy_credit", "set_payment_arrangement",
    };

    protected override ChatMessage OnCustomerTurn(Transcript t, ChatOptions? options)
    {
        string text = t.LastUserText;
        if (Intents.Amount(text) is { } requested && text.Contains("credit", StringComparison.OrdinalIgnoreCase))
        {
            return Call("apply_courtesy_credit", new { amount = requested, reason = "Customer-requested credit" });
        }

        if (t.Latest("get_current_bill") is { ValueKind: JsonValueKind.Object } bill)
        {
            bool offerArrangement = Bool(bill, "overdue") && t.LatestSuccess("set_payment_arrangement", "created") is null;
            decimal? lateFee = LateFee(bill);
            bool offerWaiver = !offerArrangement && lateFee is not null && t.Latest("apply_courtesy_credit") is null;

            if ((offerArrangement || offerWaiver) && Intents.IsNegative(text))
            {
                return Say("Understood, I haven't made any changes. Is there anything else on your bill I can explain?");
            }

            if (offerArrangement && Intents.IsAffirmative(text))
            {
                return Call("set_payment_arrangement", new { downPayment = DownPayment(bill), installments = 2 });
            }

            if (offerWaiver && Intents.IsAffirmative(text))
            {
                return Call("apply_courtesy_credit", new { amount = lateFee!.Value, reason = "Late fee waiver" });
            }
        }

        if (text.Contains("payment history", StringComparison.OrdinalIgnoreCase) || text.Contains("my payments", StringComparison.OrdinalIgnoreCase))
        {
            return Call("get_payment_history");
        }

        return Call("get_current_bill", preface: "Let me pull up your bill.");
    }

    protected override ChatMessage OnToolResult(string tool, JsonElement result, Transcript t, ChatOptions? options)
    {
        switch (tool)
        {
            case "get_current_bill":
                return Say(ExplainBill(result));

            case "get_payment_history":
                string rows = string.Join("\n", result.EnumerateArray().Select(p =>
                    $"- {Date(p, "date")}: {Money(Dec(p, "amount"))} via {Str(p, "method")}{(Bool(p, "late") ? " (late)" : "")}"));
                return Say($"Here are your recent payments:\n{rows}");

            case "apply_courtesy_credit":
                return Bool(result, "applied")
                    ? Say($"Done! A {Money(Dec(result, "amount"))} credit has been applied. Your new balance is {Money(Dec(result, "newBalance"))}. Anything else I can help with?")
                    : Say($"I'm not able to apply that. {Str(result, "message")}");

            case "set_payment_arrangement":
                if (!Bool(result, "created"))
                {
                    return Say($"I couldn't set that up: {Str(result, "message")}");
                }

                var sb = new StringBuilder($"All set. You've paid {Money(Dec(result, "downPayment"))} today, and the rest is split into {result.GetProperty("installments").GetInt32()} monthly payments of {Money(Dec(result, "installmentAmount"))}, starting {Date(result, "firstInstallmentDue")}.");
                string[] restored = result.GetProperty("restoredLines").EnumerateArray().Select(l => l.GetString()!).ToArray();
                if (restored.Length > 0)
                {
                    sb.Append($" Line {string.Join(", ", restored.Select(Last4))} is active again. Restart your phone and it should reconnect within a couple of minutes.");
                }

                return Say(sb.ToString());

            default:
                return Say("Let me know what else I can explain on your bill.");
        }
    }

    private static string ExplainBill(JsonElement bill)
    {
        string period = Str(bill, "period");
        decimal total = Dec(bill, "total");
        decimal totalDue = Dec(bill, "totalDue");

        if (Bool(bill, "overdue"))
        {
            decimal pastDue = Dec(bill, "pastDue");
            decimal down = DownPayment(bill);
            decimal installment = Math.Round((totalDue - down) / 2, 2);
            return $"Your balance of {Money(totalDue)} is past due: {Money(pastDue)} carried over from last month plus the {period} bill of {Money(total)} (due {Date(bill, "dueDate")}). "
                + "That's why service was suspended. I can set up a payment arrangement: "
                + $"{Money(down)} today and the remaining {Money(totalDue - down)} over 2 monthly payments of {Money(installment)}, and your service is restored right away. Shall I set that up?";
        }

        var sb = new StringBuilder($"Your {period} bill is {Money(total)}");
        JsonElement newCharges = bill.GetProperty("newCharges");
        if (bill.TryGetProperty("previousTotal", out JsonElement prev) && prev.ValueKind == JsonValueKind.Number && newCharges.GetArrayLength() > 0)
        {
            decimal previous = prev.GetDecimal();
            sb.Append($", up {Money(total - previous)} from last month ({Money(previous)}). The difference comes from:\n");
            foreach (JsonElement c in newCharges.EnumerateArray())
            {
                string line = Str(c, "line");
                sb.Append($"- {Str(c, "description")}: {Money(Dec(c, "amount"))}{(line.Length > 0 ? $" (line {Last4(line)})" : "")}\n");
            }
        }
        else
        {
            sb.Append(", the same as last month.\n");
        }

        if (LateFee(bill) is { } fee)
        {
            if (bill.TryGetProperty("lastPayment", out JsonElement pay) && pay.ValueKind == JsonValueKind.Object && Bool(pay, "late"))
            {
                sb.Append($"The late fee is there because last month's payment arrived on {Date(pay, "date")}, after the due date. ");
            }

            sb.Append($"As a one-time courtesy I can waive the {Money(fee)} late fee. Would you like me to do that?");
        }
        else
        {
            sb.Append($"Your total due is {Money(totalDue)}, payable by {Date(bill, "dueDate")}.");
        }

        return sb.ToString();
    }

    private static decimal? LateFee(JsonElement bill) =>
        bill.GetProperty("newCharges").EnumerateArray()
            .Where(c => Str(c, "description").Contains("Late", StringComparison.OrdinalIgnoreCase))
            .Select(c => (decimal?)Dec(c, "amount"))
            .FirstOrDefault();

    private static decimal DownPayment(JsonElement bill) => Math.Ceiling(Dec(bill, "totalDue") * 0.25m / 10m) * 10m;
}
