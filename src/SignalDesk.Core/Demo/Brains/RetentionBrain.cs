using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;

namespace SignalDesk.Core.Demo.Brains;

internal sealed class RetentionBrain() : DemoBrain(AgentRoster.Retention)
{
    protected override IReadOnlySet<string> OwnTools { get; } = new HashSet<string>
    {
        "get_retention_offers", "apply_retention_offer", "schedule_cancellation",
    };

    protected override ChatMessage OnCustomerTurn(Transcript t, ChatOptions? options)
    {
        string text = t.LastUserText;
        if (t.Latest("get_retention_offers") is not { ValueKind: JsonValueKind.Object } offers)
        {
            return Call("get_retention_offers");
        }

        bool settled = t.LatestSuccess("apply_retention_offer", "applied") is not null || t.LatestSuccess("schedule_cancellation", "scheduled") is not null;
        if (!settled)
        {
            JsonElement[] list = offers.GetProperty("offers").EnumerateArray().ToArray();
            if (Intents.IsNegative(text) || Regex.IsMatch(text, @"just cancel|cancel (it|anyway)|still (want|like) to (cancel|leave)", RegexOptions.IgnoreCase))
            {
                return Call("schedule_cancellation", new { reason = Reason(t) });
            }

            JsonElement? pick = Regex.IsMatch(text, @"upgrade|plus|second|2\b", RegexOptions.IgnoreCase) ? list.FirstOrDefault(o => Str(o, "id").StartsWith("upgrade", StringComparison.Ordinal))
                : Regex.IsMatch(text, @"discount|loyalty|first|1\b|\$", RegexOptions.IgnoreCase) || Intents.IsAffirmative(text) ? list[0]
                : null;
            if (pick is { ValueKind: JsonValueKind.Object } offer)
            {
                return Call("apply_retention_offer", new { offerId = Str(offer, "id") });
            }
        }

        return Say("Would one of those offers work for you, or would you still like to cancel?");
    }

    protected override ChatMessage OnToolResult(string tool, JsonElement result, Transcript t, ChatOptions? options)
    {
        switch (tool)
        {
            case "get_retention_offers":
                int years = result.GetProperty("tenureYears").GetInt32();
                string list = string.Join("\n", result.GetProperty("offers").EnumerateArray()
                    .Select((o, i) => $"{i + 1}. {Str(o, "title")}: {Str(o, "details")}"));
                return Say($"I'm sorry to hear you're thinking of leaving. You've been with us {years} years and we'd really like to keep you. "
                    + $"Before you decide, here's what I can offer:\n{list}\nWould either of these change your mind?");

            case "apply_retention_offer":
                return Say($"Wonderful, I'm glad you're staying! {Str(result, "message")} There's nothing else you need to do.");

            case "schedule_cancellation":
                string notes = string.Join("\n", result.GetProperty("notes").EnumerateArray().Select(n => $"- {n.GetString()}"));
                return Say($"Understood. Your cancellation is scheduled for {Date(result, "effectiveDate")}.\n{notes}\nThanks for being with Aurora Mobile. You're always welcome back.");

            default:
                return Say("Is there anything else I can help with?");
        }
    }

    private string Reason(Transcript t)
    {
        string topic = t.TopicFor(Key);
        return Regex.IsMatch(topic, @"cheap|price|expensive|cost|deal", RegexOptions.IgnoreCase) ? "Found a cheaper offer elsewhere" : "Customer request";
    }
}
