using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;

namespace SignalDesk.Core.Demo;

/// <summary>
/// Deterministic stand-in for an LLM. Each brain looks at the conversation it is given and returns exactly what a
/// model would: text, a tool call, or a handoff call. The real Agent Framework loop executes everything else.
/// </summary>
internal abstract class DemoBrain(string key)
{
    protected static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public string Key { get; } = key;

    /// <summary>Tool names this agent owns; results from other agents' tools are treated as context only.</summary>
    protected abstract IReadOnlySet<string> OwnTools { get; }

    public virtual ChatMessage Respond(Transcript t, ChatOptions? options)
    {
        if (t.PendingResult is { } pending && OwnTools.Contains(pending.Tool))
        {
            if (Transcript.IsRejected(pending.Result))
            {
                return Say("No problem, I haven't made that change. Is there anything else I can help with?");
            }

            if (Transcript.ErrorOf(pending.Result) is { } error)
            {
                return Say($"I wasn't able to complete that: {error}");
            }

            return OnToolResult(pending.Tool, pending.Result, t, options);
        }

        string? intent = Intents.Classify(t.LastUserText);
        if (intent is not null && intent != Key && !OwnsFollowUp(t) && !t.HandedOffThisTurn(Key))
        {
            AgentSpec target = AgentRoster.Get(intent);
            return Handoff(options, intent, $"Customer asked about {target.Scope}",
                $"That's one for our {target.DisplayName} team, let me bring them in.");
        }

        if (intent is null && Intents.IsClosing(t.LastUserText))
        {
            return Say("You're welcome! Thanks for choosing Aurora Mobile. Have a great day.");
        }

        return OnCustomerTurn(t, options);
    }

    protected abstract ChatMessage OnCustomerTurn(Transcript t, ChatOptions? options);

    protected abstract ChatMessage OnToolResult(string tool, JsonElement result, Transcript t, ChatOptions? options);

    /// <summary>True when the latest message answers a question this agent asked, even if it mentions another topic.</summary>
    protected virtual bool OwnsFollowUp(Transcript t) => false;

    protected static ChatMessage Say(string text) => new(ChatRole.Assistant, text);

    protected static ChatMessage Call(string tool, object? args = null, string? preface = null)
    {
        var call = new FunctionCallContent(NewCallId(), tool, ToArgs(args));
        return preface is null
            ? new ChatMessage(ChatRole.Assistant, [call])
            : new ChatMessage(ChatRole.Assistant, [new TextContent(preface), call]);
    }

    protected static ChatMessage Handoff(ChatOptions? options, string targetKey, string reason, string? preface = null)
    {
        AITool? tool = options?.Tools?.FirstOrDefault(x => x.Description.Contains($"({targetKey})", StringComparison.Ordinal));
        if (tool is null)
        {
            return Say(preface ?? $"Please hold while I find someone from {AgentRoster.Get(targetKey).DisplayName}.");
        }

        var call = new FunctionCallContent(NewCallId(), tool.Name, new Dictionary<string, object?> { ["reasonForHandoff"] = reason });
        return preface is null
            ? new ChatMessage(ChatRole.Assistant, [call])
            : new ChatMessage(ChatRole.Assistant, [new TextContent(preface + "\n\n"), call]);
    }

    protected static string Money(decimal value) => value.ToString("C", Us);

    protected static decimal Dec(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    protected static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    protected static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    protected static string Date(JsonElement e, string name) =>
        DateOnly.TryParse(Str(e, name), CultureInfo.InvariantCulture, out DateOnly d) ? d.ToString("MMM d", Us) : Str(e, name);

    protected static string Last4(string number) => number.Length >= 4 ? $"***{number[^4..]}" : number;

    private static string NewCallId() => "call_" + Guid.NewGuid().ToString("N")[..12];

    private static Dictionary<string, object?> ToArgs(object? args) =>
        args is null ? [] : JsonSerializer.SerializeToElement(args, AgentRoster.Json)
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value);
}
