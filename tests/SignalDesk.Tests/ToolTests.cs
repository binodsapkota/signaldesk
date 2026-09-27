using SignalDesk.Core.Domain;
using SignalDesk.Core.Tools;

namespace SignalDesk.Tests;

public class ToolTests
{
    private static (DeskContext Ctx, IdentityTools Identity) NewDesk()
    {
        var ctx = new DeskContext(TelcoData.CreateSeed());
        return (ctx, new IdentityTools(ctx));
    }

    [Fact]
    public void Account_tools_refuse_until_customer_is_verified()
    {
        var (ctx, _) = NewDesk();

        Assert.IsType<ToolError>(new BillingTools(ctx).GetCurrentBill());
        Assert.IsType<ToolError>(new AccountTools(ctx).GetAccountProfile());
        Assert.IsType<ToolError>(new TechSupportTools(ctx).RunLineDiagnostics("fiber"));
        Assert.IsType<ToolError>(new RetentionTools(ctx).GetRetentionOffers());
    }

    [Fact]
    public void Verification_accepts_any_phone_format_and_scopes_the_session()
    {
        var (ctx, identity) = NewDesk();

        var result = Assert.IsType<VerificationResult>(identity.VerifyCustomer("(415) 555 0142", "4821"));

        Assert.True(result.Verified);
        Assert.Equal("A-1001", ctx.Customer!.AccountId);
        Assert.IsType<BillSummary>(new BillingTools(ctx).GetCurrentBill());
    }

    [Fact]
    public void Verification_locks_after_three_failures()
    {
        var (ctx, identity) = NewDesk();

        for (int i = 0; i < DeskContext.MaxVerificationAttempts; i++)
        {
            Assert.False(Assert.IsType<VerificationResult>(identity.VerifyCustomer("415-555-0142", "0000")).Verified);
        }

        Assert.True(ctx.IsLocked);
        Assert.IsType<ToolError>(identity.VerifyCustomer("415-555-0142", "4821"));
        Assert.Null(ctx.Customer);
    }

    [Fact]
    public void Verified_session_cannot_switch_to_another_account()
    {
        var (_, identity) = NewDesk();
        identity.VerifyCustomer("415-555-0142", "4821");

        Assert.IsType<ToolError>(identity.VerifyCustomer("206-555-0123", "5550"));
    }

    [Fact]
    public void Bill_summary_isolates_new_charges()
    {
        var (ctx, identity) = NewDesk();
        identity.VerifyCustomer("415-555-0142", "4821");

        var bill = Assert.IsType<BillSummary>(new BillingTools(ctx).GetCurrentBill());

        Assert.Equal(239.49m, bill.Total);
        Assert.Equal(192m, bill.PreviousTotal);
        Assert.Equal([40m, 7.49m], bill.NewCharges.Select(c => c.Amount));
        Assert.False(bill.Overdue);
    }

    [Fact]
    public void Courtesy_credit_is_capped_by_policy_in_code()
    {
        var (ctx, identity) = NewDesk();
        identity.VerifyCustomer("415-555-0142", "4821");
        var billing = new BillingTools(ctx);

        var tooBig = Assert.IsType<CreditResult>(billing.ApplyCourtesyCredit(100m, "Goodwill"));
        var ok = Assert.IsType<CreditResult>(billing.ApplyCourtesyCredit(20m, "Goodwill"));
        var overRemaining = Assert.IsType<CreditResult>(billing.ApplyCourtesyCredit(10m, "Goodwill"));

        Assert.False(tooBig.Applied);
        Assert.True(ok.Applied);
        Assert.Equal(5m, ok.RemainingAllowance);
        Assert.False(overRemaining.Applied);
        Assert.Single(ctx.Customer!.Credits);
    }

    [Fact]
    public void Payment_arrangement_enforces_minimum_and_restores_suspended_line()
    {
        var (ctx, identity) = NewDesk();
        identity.VerifyCustomer("312-555-0177", "1937");
        var billing = new BillingTools(ctx);

        var tooLow = Assert.IsType<ArrangementResult>(billing.SetPaymentArrangement(5m, 2));
        var created = Assert.IsType<ArrangementResult>(billing.SetPaymentArrangement(30m, 2));

        Assert.False(tooLow.Created);
        Assert.True(created.Created);
        Assert.Equal(28.20m, created.InstallmentAmount);
        Assert.Equal(["312-555-0177"], created.RestoredLines);
        Assert.Equal(LineStatus.Active, ctx.Customer!.Lines[0].Status);
    }

    [Fact]
    public void Diagnostics_surface_suspension_and_reset_detects_physical_fault()
    {
        var (ctx, identity) = NewDesk();
        identity.VerifyCustomer("415-555-0142", "4821");
        var tech = new TechSupportTools(ctx);

        var fiber = Assert.IsType<DiagnosticsResult>(tech.RunLineDiagnostics("fiber"));
        var reset = Assert.IsType<ResetResult>(tech.ResetConnection("fiber"));

        Assert.Equal("degraded", fiber.Status);
        Assert.False(reset.Resolved);
        Assert.Equal(TechSupportTools.LatestFirmware, ctx.Customer!.Fiber!.RouterFirmware);

        var (ctx2, identity2) = NewDesk();
        identity2.VerifyCustomer("312-555-0177", "1937");
        var mobile = Assert.IsType<DiagnosticsResult>(new TechSupportTools(ctx2).RunLineDiagnostics("mobile"));
        Assert.Equal("suspended", mobile.Status);
        Assert.Equal("non-payment", mobile.SuspendReason);
    }

    [Theory]
    [InlineData("Leo", "415-555-0199")]
    [InlineData("son", "415-555-0199")]
    [InlineData("0142", "415-555-0142")]
    [InlineData(null, "415-555-0142")]
    public void Lines_resolve_by_name_relationship_or_digits(string? reference, string expected)
    {
        var (ctx, identity) = NewDesk();
        identity.VerifyCustomer("415-555-0142", "4821");

        Assert.Equal(expected, ctx.ResolveLine(ctx.Customer!, reference)!.Number);
    }

    [Fact]
    public void Lost_device_suspends_line_and_blocks_imei()
    {
        var (ctx, identity) = NewDesk();
        identity.VerifyCustomer("415-555-0142", "4821");

        var result = Assert.IsType<LostDeviceResult>(new AccountTools(ctx).ReportLostDevice("Leo"));

        Line leo = ctx.Customer!.Lines[1];
        Assert.True(result.Suspended);
        Assert.Equal(LineStatus.Suspended, leo.Status);
        Assert.True(leo.DeviceBlocked);
        Assert.Equal(LineStatus.Active, ctx.Customer.Lines[0].Status);
    }

    [Fact]
    public void Travel_options_depend_on_destination()
    {
        var (ctx, identity) = NewDesk();
        identity.VerifyCustomer("206-555-0123", "5550");
        var plans = new PlansTools(ctx);

        var japan = Assert.IsType<TravelOptionsResult>(plans.GetTravelOptions("Japan"));
        var canada = Assert.IsType<TravelOptionsResult>(plans.GetTravelOptions("Canada"));

        Assert.Contains(japan.Options, o => o.Id == "global_30");
        Assert.DoesNotContain(canada.Options, o => o.Id == "global_30");
    }

    [Fact]
    public void Retention_offers_scale_with_tenure()
    {
        var (ctx, identity) = NewDesk();
        identity.VerifyCustomer("206-555-0123", "5550");
        var loyal = Assert.IsType<RetentionOffersResult>(new RetentionTools(ctx).GetRetentionOffers());

        var (ctx2, identity2) = NewDesk();
        identity2.VerifyCustomer("312-555-0177", "1937");
        var newer = Assert.IsType<RetentionOffersResult>(new RetentionTools(ctx2).GetRetentionOffers());

        Assert.Equal(2, loyal.Offers.Count);
        Assert.Single(newer.Offers);
    }
}
