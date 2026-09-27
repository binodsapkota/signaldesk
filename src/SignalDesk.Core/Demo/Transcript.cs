using System.Text.Json;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;

namespace SignalDesk.Core.Demo;

/// <summary>Read-only view over the conversation an agent receives, with helpers for finding tool results.</summary>
internal sealed class Transcript
{
    private readonly Dictionary<string, string> _callNames = [];
    private readonly List<(int Index, string Tool, JsonElement Result)> _results = [];

    public Transcript(IEnumerable<ChatMessage> messages)
    {
        Messages = messages.ToList();
        LastUserIndex = -1;
        for (int i = 0; i < Messages.Count; i++)
        {
            ChatMessage m = Messages[i];
            if (IsCustomer(m))
            {
                LastUserIndex = i;
            }

            foreach (AIContent c in m.Contents)
            {
                if (c is FunctionCallContent call)
                {
                    _callNames[call.CallId] = call.Name;
                }
                else if (c is FunctionResultContent result && _callNames.TryGetValue(result.CallId, out string? name))
                {
                    _results.Add((i, name, ToJson(result.Result)));
                }
            }
        }
    }

    public IReadOnlyList<ChatMessage> Messages { get; }

    public int LastUserIndex { get; }

    public string LastUserText => LastUserIndex >= 0 ? Messages[LastUserIndex].Text : "";

    public IEnumerable<string> UserTextsNewestFirst => Messages.Where(IsCustomer).Select(m => m.Text).Reverse();

    /// <summary>
    /// Customer messages are user-role messages without an agent author. During handoffs the framework replays other
    /// agents' replies to the next agent as user-role messages tagged with the author's name.
    /// </summary>
    private static bool IsCustomer(ChatMessage m) =>
        m.Role == ChatRole.User && !string.IsNullOrWhiteSpace(m.Text) &&
        (m.AuthorName is null || AgentRoster.All.All(a => a.Key != m.AuthorName));

    /// <summary>The tool result in the final message, when the model is being asked to continue after a tool call.</summary>
    public (string Tool, JsonElement Result)? PendingResult =>
        _results.Count > 0 && _results[^1].Index == Messages.Count - 1 ? (_results[^1].Tool, _results[^1].Result) : null;

    public JsonElement? Latest(string tool)
    {
        for (int i = _results.Count - 1; i >= 0; i--)
        {
            if (_results[i].Tool == tool) return _results[i].Result;
        }

        return null;
    }

    /// <summary>Latest result for a tool that completed successfully (not refused, rejected or errored).</summary>
    public JsonElement? LatestSuccess(string tool, string flag)
    {
        JsonElement? r = Latest(tool);
        return r is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(flag, out JsonElement f) && f.ValueKind == JsonValueKind.True ? e : null;
    }

    public bool CalledThisTurn(string tool) => _results.Any(r => r.Index > LastUserIndex && r.Tool == tool);

    /// <summary>True when another agent already acted on the latest customer message and routed it here.</summary>
    public bool HandedOffThisTurn(string agentKey) =>
        Messages.Skip(LastUserIndex + 1).Any(m => m.AuthorName is { } author && author != agentKey);

    /// <summary>Newest customer message whose topic belongs to the given agent, falling back to the latest message.</summary>
    public string TopicFor(string agentKey) =>
        UserTextsNewestFirst.FirstOrDefault(t => Intents.Classify(t) == agentKey) ?? LastUserText;

    /// <summary>Newest customer message that expresses any specialist intent.</summary>
    public string? LatestIntent(out string? agentKey)
    {
        foreach (string text in UserTextsNewestFirst)
        {
            agentKey = Intents.Classify(text);
            if (agentKey is not null) return text;
        }

        agentKey = null;
        return null;
    }

    public static bool IsRejected(JsonElement result) =>
        result.ValueKind == JsonValueKind.String && (result.GetString() ?? "").Contains("rejected", StringComparison.OrdinalIgnoreCase);

    public static string? ErrorOf(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object && result.TryGetProperty("error", out JsonElement e) ? e.GetString() : null;

    private static JsonElement ToJson(object? value) => value switch
    {
        JsonElement e => e,
        null => JsonSerializer.SerializeToElement<string?>(null),
        _ => JsonSerializer.SerializeToElement(value, value.GetType(), AgentRoster.Json),
    };
}
