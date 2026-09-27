using System.ComponentModel;
using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Tools;

public sealed record PlanOption(string Id, string Name, decimal MonthlyPrice, string Data, string Highlights);

public sealed record PlanCatalogResult(IReadOnlyList<PlanOption> MobilePlans, IReadOnlyList<LineInfo> CurrentLines);

public sealed record TravelOptionsResult(string Destination, bool IncludedInPlan, IReadOnlyList<TravelPass> Options, string Recommendation);

public sealed record PassResult(bool Added, string PassId, string PassName, decimal Price, string Line, string Message);

public sealed record PlanChangeResult(bool Changed, string Line, string OldPlan, string NewPlan, decimal MonthlyDifference, string Message);

public sealed class PlansTools(DeskContext ctx)
{
    [Description("List the mobile plans on offer alongside each line's current plan.")]
    public object ListPlans()
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        return new PlanCatalogResult(
            Catalog.Plans.Where(p => !p.IsFiber).Select(p => new PlanOption(p.Id, p.Name, p.MonthlyPrice, p.Data, p.Highlights)).ToList(),
            c.Lines.Select(AccountTools.Describe).ToList());
    }

    [Description("Get international travel options for a destination country, based on the customer's plan.")]
    public object GetTravelOptions(
        [Description("Destination country, e.g. Japan")] string destination,
        [Description("Line that will travel (defaults to the account holder's line)")] string? lineNumber = null)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        Line? line = ctx.ResolveLine(c, lineNumber);
        if (line is null) return new ToolError($"No line matching '{lineNumber}' on this account.");

        string dest = destination.Trim();
        if (line.PlanId == "unlimited_max")
        {
            return new TravelOptionsResult(dest, true, [], $"{Catalog.FindPlan(line.PlanId)!.Name} already includes travel data in {dest}.");
        }

        bool northAmerica = Catalog.NorthAmerica.Contains(dest.ToLowerInvariant());
        List<TravelPass> options = northAmerica
            ? [Catalog.FindPass("day_pass")!]
            : [.. Catalog.TravelPasses];
        string recommendation = northAmerica
            ? "TravelPass Day is the only option for North America; it is only charged on days the phone is used."
            : "For trips longer than 5 days the Global 30-Day Pass is cheaper than TravelPass Day.";
        return new TravelOptionsResult(dest, false, options, recommendation);
    }

    [Description("Add an international travel pass to a line. Requires approval because it adds a charge.")]
    public object AddInternationalPass(
        [Description("Pass id from get_travel_options, e.g. global_30")] string passId,
        [Description("Line to add it to (defaults to the account holder's line)")] string? lineNumber = null)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        TravelPass? pass = Catalog.FindPass(passId);
        if (pass is null) return new ToolError($"Unknown pass '{passId}'.");
        Line? line = ctx.ResolveLine(c, lineNumber);
        if (line is null) return new ToolError($"No line matching '{lineNumber}' on this account.");
        if (line.TravelPasses.Contains(pass.Id)) return new ToolError($"{pass.Name} is already active on {line.Number}.");

        line.TravelPasses.Add(pass.Id);
        ctx.Record("travel_pass_added", $"{pass.Name} on {line.Number}");
        return new PassResult(true, pass.Id, pass.Name, pass.Price, line.Number, $"{pass.Name} is active and starts when the phone connects abroad.");
    }

    [Description("Change the plan on a mobile line. Takes effect immediately with prorated billing. Requires approval.")]
    public object ChangePlan(
        [Description("Target plan id from list_plans, e.g. unlimited_plus")] string planId,
        [Description("Line to change (defaults to the account holder's line)")] string? lineNumber = null)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        Plan? plan = Catalog.FindPlan(planId);
        if (plan is null || plan.IsFiber) return new ToolError($"Unknown mobile plan '{planId}'.");
        Line? line = ctx.ResolveLine(c, lineNumber);
        if (line is null) return new ToolError($"No line matching '{lineNumber}' on this account.");

        Plan old = Catalog.FindPlan(line.PlanId)!;
        if (old.Id == plan.Id) return new ToolError($"{line.Number} is already on {plan.Name}.");

        line.PlanId = plan.Id;
        ctx.Record("plan_changed", $"{line.Number}: {old.Name} -> {plan.Name}");
        return new PlanChangeResult(true, line.Number, old.Name, plan.Name, plan.MonthlyPrice - old.MonthlyPrice,
            $"{line.Number} moved to {plan.Name}. The difference is prorated on the next bill.");
    }
}
