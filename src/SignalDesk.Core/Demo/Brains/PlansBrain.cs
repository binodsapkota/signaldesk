using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;
using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Demo.Brains;

internal sealed class PlansBrain() : DemoBrain(AgentRoster.Plans)
{
    private static readonly string[] Tiers = ["essentials", "unlimited", "unlimited_plus", "unlimited_max"];

    protected override IReadOnlySet<string> OwnTools { get; } = new HashSet<string>
    {
        "list_plans", "get_travel_options", "add_international_pass", "change_plan",
    };

    protected override ChatMessage OnCustomerTurn(Transcript t, ChatOptions? options)
    {
        string text = t.LastUserText;
        string topic = t.TopicFor(Key);

        if (IsTravel(t, topic, out string destination))
        {
            if (t.Latest("get_travel_options") is { ValueKind: JsonValueKind.Object } travel && t.LatestSuccess("add_international_pass", "added") is null)
            {
                if (Intents.IsNegative(text)) return Say("No problem, nothing has been added. Have a great trip!");

                string? pass = PassChoice(text) ?? (Intents.IsAffirmative(text) ? RecommendedPass(travel, TripDays(t)) : null);
                if (pass is not null) return Call("add_international_pass", new { passId = pass });
            }

            return Call("get_travel_options", new { destination });
        }

        if (t.Latest("list_plans") is { ValueKind: JsonValueKind.Object } catalog && t.LatestSuccess("change_plan", "changed") is null)
        {
            if (Intents.IsNegative(text)) return Say("No problem, your plan stays as it is.");

            string? plan = PlanMentioned(text) ?? (Intents.IsAffirmative(text) ? Recommended(catalog, topic) : null);
            if (plan is not null) return Call("change_plan", new { planId = plan });
        }

        return Call("list_plans");
    }

    protected override ChatMessage OnToolResult(string tool, JsonElement result, Transcript t, ChatOptions? options)
    {
        switch (tool)
        {
            case "get_travel_options":
                string dest = Str(result, "destination");
                if (Bool(result, "includedInPlan"))
                {
                    return Say($"Good news: your plan already includes travel data in {dest}, so there's nothing to add. Enjoy the trip!");
                }

                string opts = string.Join("\n", result.GetProperty("options").EnumerateArray()
                    .Select(o => $"- {Str(o, "name")}: {Money(Dec(o, "price"))} ({Str(o, "details")})"));
                int? days = TripDays(t);
                string recommended = RecommendedPass(result, days);
                string recName = Catalog.FindPass(recommended)!.Name;
                string why = days is { } d && recommended == "global_30"
                    ? $"For {d} days, TravelPass Day could cost up to {Money(d * 10m)}, so the {recName} at $50 is the better deal."
                    : Str(result, "recommendation");
                return Say($"Here are your options for {dest}:\n{opts}\n{why} Want me to add the {recName} to your line?");

            case "list_plans":
                JsonElement line = result.GetProperty("currentLines")[0];
                string current = Str(line, "plan");
                string target = Recommended(result, t.TopicFor(Key));
                JsonElement plan = result.GetProperty("mobilePlans").EnumerateArray().First(p => Str(p, "id") == target);
                decimal currentPrice = result.GetProperty("mobilePlans").EnumerateArray().First(p => Str(p, "name") == current).GetProperty("monthlyPrice").GetDecimal();
                decimal diff = Dec(plan, "monthlyPrice") - currentPrice;
                return Say($"You're on {current} ({Money(currentPrice)}/mo). I'd suggest {Str(plan, "name")} at {Money(Dec(plan, "monthlyPrice"))}/mo "
                    + $"({(diff >= 0 ? "+" : "-")}{Money(Math.Abs(diff))}): {Str(plan, "highlights")}. Want me to switch you?");

            case "add_international_pass":
                return Say($"Done: {Str(result, "passName")} ({Money(Dec(result, "price"))}) is on line {Last4(Str(result, "line"))}. {Str(result, "message")} Safe travels!");

            case "change_plan":
                decimal delta = Dec(result, "monthlyDifference");
                return Say($"You're now on {Str(result, "newPlan")} (was {Str(result, "oldPlan")}), {(delta >= 0 ? "+" : "-")}{Money(Math.Abs(delta))}/mo. {Str(result, "message")}");

            default:
                return Say("Anything else about plans or travel I can help with?");
        }
    }

    private static bool IsTravel(Transcript t, string topic, out string destination)
    {
        destination = Intents.Country(topic) ?? t.UserTextsNewestFirst.Select(Intents.Country).FirstOrDefault(c => c is not null) ?? "International";
        return destination != "International" || Regex.IsMatch(topic, @"travel|trip|international|roaming|abroad|\bpass\b", RegexOptions.IgnoreCase);
    }

    private static int? TripDays(Transcript t) =>
        t.UserTextsNewestFirst.Select(x => Regex.Match(x, @"(\d{1,2})\s*(days|day)")).FirstOrDefault(m => m.Success) is { } m
            ? int.Parse(m.Groups[1].Value) : null;

    private static string RecommendedPass(JsonElement travel, int? days)
    {
        bool hasGlobal = travel.GetProperty("options").EnumerateArray().Any(o => Str(o, "id") == "global_30");
        return hasGlobal && (days is null || days > 5) ? "global_30" : "day_pass";
    }

    private static string? PassChoice(string text)
    {
        if (Regex.IsMatch(text, @"30|global|month", RegexOptions.IgnoreCase)) return "global_30";
        if (Regex.IsMatch(text, @"day pass|daily|per day", RegexOptions.IgnoreCase)) return "day_pass";
        return null;
    }

    private static string? PlanMentioned(string text)
    {
        string s = text.ToLowerInvariant();
        if (s.Contains("max")) return "unlimited_max";
        if (s.Contains("plus")) return "unlimited_plus";
        if (s.Contains("essentials")) return "essentials";
        return null;
    }

    private static string Recommended(JsonElement catalog, string topic)
    {
        string currentName = Str(catalog.GetProperty("currentLines")[0], "plan");
        string currentId = catalog.GetProperty("mobilePlans").EnumerateArray().First(p => Str(p, "name") == currentName).GetProperty("id").GetString()!;
        int index = Array.IndexOf(Tiers, currentId);
        bool cheaper = Regex.IsMatch(topic, @"cheaper|downgrade|lower|save money", RegexOptions.IgnoreCase);
        return PlanMentioned(topic) ?? Tiers[Math.Clamp(index + (cheaper ? -1 : 1), 0, Tiers.Length - 1)];
    }
}
