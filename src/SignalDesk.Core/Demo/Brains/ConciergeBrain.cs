using System.Text.Json;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;

namespace SignalDesk.Core.Demo.Brains;

internal sealed class ConciergeBrain() : DemoBrain(AgentRoster.Concierge)
{
    protected override IReadOnlySet<string> OwnTools { get; } = new HashSet<string> { "verify_customer" };

    public override ChatMessage Respond(Transcript t, ChatOptions? options)
    {
        if (t.PendingResult is { Tool: "verify_customer" } pending)
        {
            return OnToolResult(pending.Tool, pending.Result, t, options);
        }

        return OnCustomerTurn(t, options);
    }

    protected override ChatMessage OnCustomerTurn(Transcript t, ChatOptions? options)
    {
        string text = t.LastUserText;
        bool verified = t.LatestSuccess("verify_customer", "verified") is not null;
        string? intent = Intents.Classify(text);

        if (Faq(text) is { } answer && intent is null)
        {
            return Say(answer);
        }

        if (!verified)
        {
            string? phone = Intents.Phone(text);
            string? pin = Intents.Pin(text);
            if (pin is not null)
            {
                phone ??= t.UserTextsNewestFirst.Select(Intents.Phone).FirstOrDefault(p => p is not null);
            }

            if (phone is not null && pin is not null)
            {
                return Call("verify_customer", new { phoneNumber = phone, pin });
            }

            if (phone is not null)
            {
                return Say("Thanks. For security I also need the 4-digit PIN on the account.");
            }

            if (intent is null && Intents.IsClosing(text))
            {
                return Say("Thanks for contacting Aurora Mobile. Have a great day!");
            }

            t.LatestIntent(out string? topicAgent);
            return topicAgent is not null
                ? Say($"I can help get that sorted with our {AgentRoster.Get(topicAgent).DisplayName} team. First, please confirm the phone number on the account and your 4-digit account PIN.")
                : Say("Hi, welcome to Aurora Mobile support! I can help with home internet, mobile service, bills, plans, travel and account changes. What can I do for you today?");
        }

        if (intent is not null)
        {
            return Route(options, intent, preface: null);
        }

        return Intents.IsClosing(text)
            ? Say("Thanks for contacting Aurora Mobile. Have a great day!")
            : Say("What else can I help you with today?");
    }

    protected override ChatMessage OnToolResult(string tool, JsonElement result, Transcript t, ChatOptions? options)
    {
        if (Transcript.ErrorOf(result) is { } error)
        {
            return Say(error);
        }

        if (!Bool(result, "verified"))
        {
            int remaining = result.GetProperty("attemptsRemaining").GetInt32();
            return remaining > 0
                ? Say($"I couldn't match that phone number and PIN. Please double-check and try again ({remaining} attempt{(remaining == 1 ? "" : "s")} left).")
                : Say("That didn't match either, so I've locked verification to protect the account. Please visit an Aurora store with photo ID and we'll sort it out there.");
        }

        string first = Str(result, "customerName").Split(' ')[0];
        if (t.LatestIntent(out string? agent) is not null && agent is not null)
        {
            return Route(options, agent, $"Thanks, {first}, you're verified.");
        }

        return Say($"Thanks, {first}, you're verified. What can I help you with today?");
    }

    private static ChatMessage Route(ChatOptions? options, string agent, string? preface)
    {
        AgentSpec target = AgentRoster.Get(agent);
        string handoffLine = $"I'm connecting you with {target.DisplayName}, who can take it from here.";
        return Handoff(options, agent, $"Verified customer needs help with {target.Scope}",
            preface is null ? handoffLine : $"{preface} {handoffLine}");
    }

    private static string? Faq(string text)
    {
        if (text.Contains("hours", StringComparison.OrdinalIgnoreCase) || text.Contains("open", StringComparison.OrdinalIgnoreCase) && text.Contains("store", StringComparison.OrdinalIgnoreCase))
        {
            return "Most Aurora stores are open 10 AM - 8 PM Monday to Saturday and 11 AM - 6 PM on Sunday. Support chat is available 24/7.";
        }

        return null;
    }
}
