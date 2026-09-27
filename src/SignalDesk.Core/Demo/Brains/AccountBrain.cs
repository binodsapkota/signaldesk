using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;

namespace SignalDesk.Core.Demo.Brains;

internal sealed class AccountBrain() : DemoBrain(AgentRoster.Account)
{
    private static readonly string[] Relationships = ["son", "daughter", "wife", "husband", "partner", "mom", "dad", "mother", "father"];

    protected override IReadOnlySet<string> OwnTools { get; } = new HashSet<string>
    {
        "get_account_profile", "report_lost_device", "update_contact_email",
    };

    protected override bool OwnsFollowUp(Transcript t) => Intents.Email(t.LastUserText) is not null;

    protected override ChatMessage OnCustomerTurn(Transcript t, ChatOptions? options) => Decide(t, fetchedNow: false);

    protected override ChatMessage OnToolResult(string tool, JsonElement result, Transcript t, ChatOptions? options)
    {
        switch (tool)
        {
            case "get_account_profile":
                return Decide(t, fetchedNow: true);

            case "report_lost_device":
                string steps = string.Join("\n", result.GetProperty("nextSteps").EnumerateArray().Select(s => $"- {s.GetString()}"));
                return Say($"Done. {Str(result, "user")}'s line {Last4(Str(result, "line"))} is suspended and the {Str(result, "device")} is blocked on every network, so nobody can use it or run up charges. Next steps:\n{steps}");

            case "update_contact_email":
                return Say($"Your contact email is now {Str(result, "newEmail")}. {Str(result, "message")}");

            default:
                return Say("Is there anything else on your account I can help with?");
        }
    }

    private ChatMessage Decide(Transcript t, bool fetchedNow)
    {
        string topic = t.TopicFor(Key);
        string text = t.LastUserText;

        if (Intents.Email(text) is { } email)
        {
            return Call("update_contact_email", new { newEmail = email });
        }

        if (Regex.IsMatch(topic, @"\bemail\b", RegexOptions.IgnoreCase) && !IsLost(topic))
        {
            return Say("Sure. What email address would you like us to use?");
        }

        if (t.Latest("get_account_profile") is not { ValueKind: JsonValueKind.Object } profile)
        {
            return Call("get_account_profile", preface: IsLost(topic) ? "I'm sorry, let's lock that phone down right away." : null);
        }

        JsonElement[] lines = profile.GetProperty("lines").EnumerateArray().ToArray();
        if (IsLost(topic) && t.LatestSuccess("report_lost_device", "suspended") is null)
        {
            JsonElement? line = MatchLine(lines, text) ?? MatchLine(lines, topic) ?? (lines.Length == 1 ? lines[0] : null);
            if (line is { } l)
            {
                return Call("report_lost_device", new { line = Str(l, "number") });
            }

            string options = string.Join(", ", lines.Select(x => $"{Str(x, "user")} ({Last4(Str(x, "number"))})"));
            return Say($"Which line is the missing phone on: {options}?");
        }

        if (fetchedNow)
        {
            string summary = string.Join("\n", lines.Select(x => $"- {Str(x, "number")}: {Str(x, "user")}, {Str(x, "plan")}, {Str(x, "device")} ({Str(x, "status")})"));
            return Say($"Here's your account ({Str(profile, "accountId")}), contact email {Str(profile, "email")}:\n{summary}\nWhat would you like to update?");
        }

        return Say("I can update your contact details or lock a lost or stolen phone. What do you need?");
    }

    private static bool IsLost(string text) => Regex.IsMatch(text, @"\bstolen\b|\blost\b|\bmissing\b", RegexOptions.IgnoreCase);

    private static JsonElement? MatchLine(JsonElement[] lines, string text)
    {
        foreach (JsonElement l in lines)
        {
            string first = Str(l, "user").Split(' ')[0];
            string relationship = Str(l, "relationship");
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(first)}\b", RegexOptions.IgnoreCase)) return l;
            if (Relationships.Contains(relationship) && Regex.IsMatch(text, $@"\b{relationship}'?s?\b", RegexOptions.IgnoreCase)) return l;
        }

        // A bare phone number is usually the one given for verification, so it is only trusted as a last resort.
        string? phone = Intents.Phone(text);
        return phone is null ? null : lines.FirstOrDefault(l => Str(l, "number").EndsWith(phone[^4..], StringComparison.Ordinal)) is { ValueKind: JsonValueKind.Object } m ? m : null;
    }
}
