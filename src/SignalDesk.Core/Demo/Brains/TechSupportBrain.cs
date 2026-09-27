using System.Text.Json;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;

namespace SignalDesk.Core.Demo.Brains;

internal sealed class TechSupportBrain() : DemoBrain(AgentRoster.TechSupport)
{
    protected override IReadOnlySet<string> OwnTools { get; } = new HashSet<string>
    {
        "check_network_status", "run_line_diagnostics", "reset_connection", "get_technician_slots", "schedule_technician",
    };

    protected override bool OwnsFollowUp(Transcript t) => AwaitingSlot(t) && SlotChoice(t.LastUserText) is not null;

    protected override ChatMessage OnCustomerTurn(Transcript t, ChatOptions? options)
    {
        string text = t.LastUserText;
        if (AwaitingSlot(t))
        {
            if (Intents.IsNegative(text))
            {
                return Say("Okay, I won't book a visit. If the drops continue, just message us and we can schedule one any time.");
            }

            if ((SlotChoice(text) ?? (Intents.IsAffirmative(text) ? "S1" : null)) is { } slot)
            {
                return Call("schedule_technician", new { slotId = slot, issueSummary = IssueSummary(t) });
            }
        }

        string service = Service(t);
        return Call("check_network_status", preface: service == "fiber"
            ? "Sorry you're dealing with that. Let me run some checks on your home fiber connection."
            : "Sorry you're dealing with that. Let me check the network and your line.");
    }

    protected override ChatMessage OnToolResult(string tool, JsonElement result, Transcript t, ChatOptions? options)
    {
        string service = Service(t);
        switch (tool)
        {
            case "check_network_status":
                JsonElement outage = result.GetProperty("outages").EnumerateArray()
                    .FirstOrDefault(o => Str(o, "service") == service);
                if (outage.ValueKind == JsonValueKind.Object)
                {
                    return Say($"There's a known incident in your area: {Str(outage, "summary")}. Our engineers expect it fixed by {Str(outage, "eta")}. I'm sorry for the disruption. Service will come back automatically, so there's nothing you need to do.");
                }

                return Call("run_line_diagnostics", new { service, lineNumber = LineRef(t) });

            case "run_line_diagnostics":
                string status = Str(result, "status");
                string target = Str(result, "target");
                if (status == "suspended")
                {
                    if (Str(result, "suspendReason") == "non-payment")
                    {
                        return Handoff(options, AgentRoster.Billing, $"Line {target} is suspended for non-payment",
                            $"Found it: there's no outage, but line {Last4(target)} is suspended for non-payment, so the network is blocking it. Billing can get you back online quickly. Transferring you now.");
                    }

                    return Say($"Line {Last4(target)} is suspended because it was reported lost or stolen. Once you have a new device, Account Services can restore it.");
                }

                string findings = string.Join("; ", result.GetProperty("findings").EnumerateArray().Select(f => f.GetString()));
                if (status == "degraded")
                {
                    return Call("reset_connection", new { service }, $"No outage in your area, but diagnostics show: {findings}. Let me try a remote reset first.");
                }

                return service == "mobile"
                    ? Call("reset_connection", new { service, lineNumber = target }, $"Your line looks healthy from our side ({findings}). I'll refresh its network provisioning to be safe.")
                    : Say($"Your connection looks healthy right now ({findings}). If it drops again, restart the router and message us. I'll have the history ready.");

            case "reset_connection":
                string actions = string.Join(" and ", result.GetProperty("actions").EnumerateArray().Select(a => Lower(a.GetString()!)));
                if (Bool(result, "resolved"))
                {
                    return Say($"Done: I {actions}. {Str(result, "outcome")}");
                }

                return Call("get_technician_slots", preface: $"I {actions}, but {Lower(Str(result, "outcome"))}");

            case "get_technician_slots":
                string slots = string.Join("\n", result.EnumerateArray().Select(s => $"- {Str(s, "label")} ({Str(s, "id")})"));
                return Say($"I can send a technician to inspect the fiber line and connectors at no charge. The next windows are:\n{slots}\nWhich one works for you?");

            case "schedule_technician":
                return Say($"You're booked for {Str(result, "slot")} (appointment {Str(result, "appointmentId")}). You'll get a text 30 minutes before the technician arrives. Is there anything else I can help with?");

            default:
                return Say("Let me know what else I can check for you.");
        }
    }

    private static bool AwaitingSlot(Transcript t) =>
        t.Latest("get_technician_slots") is not null && t.LatestSuccess("schedule_technician", "booked") is null;

    private string Service(Transcript t) => Intents.IsHomeInternet(t.TopicFor(Key)) ? "fiber" : "mobile";

    private string? LineRef(Transcript t)
    {
        string topic = t.TopicFor(Key);
        return Intents.Phone(topic);
    }

    private static string? SlotChoice(string text)
    {
        string s = text.ToLowerInvariant();
        if (s.Contains("s3") || s.Contains("wednesday") || s.Contains("third")) return "S3";
        if (s.Contains("s2") || s.Contains("afternoon") || s.Contains("second")) return "S2";
        if (s.Contains("s1") || s.Contains("morning") || s.Contains("tuesday") || s.Contains("first")) return "S1";
        return null;
    }

    private static string IssueSummary(Transcript t)
    {
        JsonElement? diag = t.Latest("run_line_diagnostics");
        return diag is { } d && d.TryGetProperty("findings", out JsonElement f)
            ? "Intermittent drops: " + string.Join("; ", f.EnumerateArray().Take(2).Select(x => x.GetString()))
            : "Intermittent connection drops";
    }

    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
