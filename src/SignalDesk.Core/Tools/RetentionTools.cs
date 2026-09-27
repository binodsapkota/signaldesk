using System.ComponentModel;
using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Tools;

public sealed record RetentionOffersResult(int TenureYears, decimal MonthlySpend, IReadOnlyList<RetentionOffer> Offers);

public sealed record OfferResult(bool Applied, string OfferId, string Title, string Message);

public sealed record CancellationResult(bool Scheduled, DateOnly EffectiveDate, decimal FinalBillEstimate, IReadOnlyList<string> Notes);

public sealed class RetentionTools(DeskContext ctx)
{
    [Description("Get the save offers this customer is eligible for, based on tenure and current spend.")]
    public object GetRetentionOffers()
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;
        return new RetentionOffersResult(c.TenureYears, c.CurrentBill.Items.Where(i => !i.Description.Contains("fee")).Sum(i => i.Amount), OffersFor(c));
    }

    [Description("Apply a save offer to the account. Requires approval.")]
    public object ApplyRetentionOffer([Description("Offer id from get_retention_offers")] string offerId)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        RetentionOffer? offer = OffersFor(c).FirstOrDefault(o => o.Id.Equals(offerId, StringComparison.OrdinalIgnoreCase));
        if (offer is null) return new ToolError($"Offer '{offerId}' is not available for this account.");
        if (c.RetentionOffer is not null) return new ToolError("A save offer has already been applied to this account.");

        c.RetentionOffer = offer.Id;
        if (offer.Id == "upgrade_plus_6")
        {
            c.Lines[0].PlanId = "unlimited_plus";
        }

        c.CancellationDate = null;
        ctx.Record("retention_offer", offer.Title);
        return new OfferResult(true, offer.Id, offer.Title, $"{offer.Title} applied. {offer.Details}");
    }

    [Description("Schedule account cancellation at the end of the current billing cycle. Requires approval.")]
    public object ScheduleCancellation([Description("Customer's stated reason for leaving")] string reason)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        var effective = new DateOnly(TelcoData.Today.Year, TelcoData.Today.Month, 1).AddMonths(1);
        c.CancellationDate = effective;
        ctx.Record("cancellation_scheduled", $"Effective {effective:MMM d} - {reason}");
        return new CancellationResult(true, effective, c.CurrentBill.Total,
        [
            "Service stays active until the effective date",
            "To keep the number, port it to the new carrier before the effective date",
            "Rented equipment (fiber router) must be returned within 30 days",
        ]);
    }

    private static List<RetentionOffer> OffersFor(Customer c) => c.TenureYears >= 3
        ? [
            new("loyalty_15", "$15/month loyalty discount", "Saves $15 every month for the next 12 months.", 15m, 12),
            new("upgrade_plus_6", "Free upgrade to Unlimited Plus", "Unlimited Plus at your current price for 6 months (50 GB hotspot, HD streaming).", 15m, 6),
        ]
        : [new("loyalty_10", "$10/month loyalty discount", "Saves $10 every month for the next 6 months.", 10m, 6)];
}
