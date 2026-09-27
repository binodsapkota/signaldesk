using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;
using SignalDesk.Core.Domain;
using SignalDesk.Core.Tools;

namespace SignalDesk.Core.Runtime;

public sealed class SessionBusyException(string message) : InvalidOperationException(message);

/// <summary>
/// One customer conversation. Owns a long-lived <see cref="StreamingRun"/> of the handoff workflow, so agent memory,
/// the active agent and pending approvals survive across HTTP requests. Turns are serialized per session.
/// </summary>
public sealed class DeskSession : IAsyncDisposable
{
    private const string HandoffPrefix = "handoff_to_";

    private readonly Workflow _workflow;
    private readonly DeskOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<TranscriptEntry> _transcript = [];
    private readonly List<string> _path = [AgentRoster.Concierge];
    private readonly Dictionary<string, string> _callNames = [];
    private StreamingRun? _run;
    private ExternalRequest? _pendingRequest;

    public DeskSession(string id, DeskOptions options, IChatClient? liveClient)
    {
        Id = id;
        _options = options;
        Context = new DeskContext(TelcoData.CreateSeed());
        _workflow = DeskWorkflowFactory.Build(Context, spec =>
            liveClient ?? new Demo.DemoChatClient(spec.Key, options.DemoTokenDelayMs));
    }

    public string Id { get; }

    public DeskContext Context { get; }

    public string ActiveAgent { get; private set; } = AgentRoster.Concierge;

    public PendingApproval? Pending { get; private set; }

    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsBusy => _gate.CurrentCount == 0;

    public SessionState Snapshot() => new(
        Id, _options.IsLive ? "openai" : "demo", ActiveAgent, [.. _path], Customer(), Pending, [.. _transcript], [.. Context.Audit]);

    public async IAsyncEnumerable<DeskEvent> SendAsync(string text, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            if (Pending is not null)
            {
                throw new InvalidOperationException("Resolve the pending approval before sending another message.");
            }

            _run ??= await InProcessExecution.OpenStreamingAsync(_workflow, cancellationToken: cancellationToken);
            _transcript.Add(new TranscriptEntry("customer", null, text, DateTimeOffset.UtcNow));

            await _run.TrySendMessageAsync(new ChatMessage(ChatRole.User, text));
            await _run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            await foreach (DeskEvent evt in PumpAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            Exit();
        }
    }

    public async IAsyncEnumerable<DeskEvent> ResolveApprovalAsync(string requestId, bool approved, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            if (Pending is null || _pendingRequest is null || Pending.RequestId != requestId)
            {
                throw new InvalidOperationException("There is no pending approval with that id.");
            }

            ExternalRequest request = _pendingRequest;
            ToolApprovalRequestContent approval = request.Data.As<ToolApprovalRequestContent>()!;
            _transcript.Add(new TranscriptEntry("approval", Pending.Agent, $"{(approved ? "Approved" : "Declined")}: {Pending.Summary}", DateTimeOffset.UtcNow));
            Pending = null;
            _pendingRequest = null;

            yield return new DeskEvent("approval_resolved", new ApprovalResolvedData(requestId, approved));

            await _run!.SendResponseAsync(request.CreateResponse(
                approval.CreateResponse(approved, approved ? null : "The customer declined this action.")));

            await foreach (DeskEvent evt in PumpAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            Exit();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_run is not null)
        {
            await _run.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async IAsyncEnumerable<DeskEvent> PumpAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        string? textAgent = null;
        string? handoffReason = null;
        int handoffs = 0;

        DeskEvent? Flush()
        {
            if (textAgent is null || text.Length == 0) return null;
            string message = text.ToString().Trim();
            string agent = textAgent;
            text.Clear();
            textAgent = null;
            if (message.Length == 0) return null;
            _transcript.Add(new TranscriptEntry("agent", agent, message, DateTimeOffset.UtcNow));
            return new DeskEvent("message", new MessageData(agent, message));
        }

        await foreach (WorkflowEvent evt in _run!.WatchStreamAsync(blockOnPendingRequest: false, cancellationToken))
        {
            switch (evt)
            {
                case ExecutorInvokedEvent invoked when ResolveAgent(invoked.ExecutorId) is { } agent && agent != ActiveAgent:
                    if (Flush() is { } flushed) yield return flushed;

                    handoffs++;
                    string from = ActiveAgent;
                    ActiveAgent = agent;
                    _path.Add(agent);
                    _transcript.Add(new TranscriptEntry("handoff", agent, $"{from} -> {agent}: {handoffReason}", DateTimeOffset.UtcNow));
                    yield return new DeskEvent("handoff", new HandoffData(from, agent, handoffReason));
                    handoffReason = null;

                    if (handoffs > _options.MaxHandoffsPerTurn)
                    {
                        // Abandon the looping run; the next message starts a fresh workflow at the Concierge.
                        await _run.DisposeAsync();
                        _run = null;
                        ActiveAgent = AgentRoster.Concierge;
                        _path.Add(AgentRoster.Concierge);
                        yield return new DeskEvent("error", new ErrorData("Routing loop detected. The conversation was reset to the Concierge; a human agent has been notified."));
                        yield return new DeskEvent("turn_end", new TurnEndData(ActiveAgent, "customer", [.. _path], Customer(), [.. Context.Audit]));
                        yield break;
                    }

                    break;

                case AgentResponseUpdateEvent update:
                    string author = ResolveAgent(update.Update.AuthorName ?? "") ?? ActiveAgent;
                    foreach (AIContent content in update.Update.Contents)
                    {
                        switch (content)
                        {
                            case TextContent { Text.Length: > 0 } token:
                                if (textAgent is not null && textAgent != author && Flush() is { } previous) yield return previous;
                                textAgent = author;
                                text.Append(token.Text);
                                yield return new DeskEvent("token", new TokenData(author, token.Text));
                                break;

                            case FunctionCallContent call when call.Name.StartsWith(HandoffPrefix, StringComparison.Ordinal):
                                if (Flush() is { } beforeHandoff) yield return beforeHandoff;
                                handoffReason = call.Arguments?.TryGetValue("reasonForHandoff", out object? r) == true ? r?.ToString() : null;
                                break;

                            case FunctionCallContent call:
                                if (Flush() is { } beforeCall) yield return beforeCall;
                                _callNames[call.CallId] = call.Name;
                                yield return new DeskEvent("tool_call", new ToolCallData(author, call.Name, call.CallId, Redaction.MaskArgs(call.Arguments)));
                                break;

                            case FunctionResultContent result when _callNames.TryGetValue(result.CallId, out string? tool):
                                JsonElement json = ToJson(result.Result);
                                yield return new DeskEvent("tool_result", new ToolResultData(author, tool, result.CallId, IsSuccess(json), json));
                                break;
                        }
                    }

                    break;

                case RequestInfoEvent request when request.Request.Data.Is(out ToolApprovalRequestContent? approval) && approval?.ToolCall is FunctionCallContent toolCall:
                    if (Flush() is { } beforeApproval) yield return beforeApproval;

                    string approver = ResolveAgent(request.Request.PortInfo.PortId) ?? ActiveAgent;
                    var args = Redaction.MaskArgs(toolCall.Arguments);
                    _pendingRequest = request.Request;
                    Pending = new PendingApproval(request.Request.RequestId, approver, toolCall.Name, args, Redaction.DescribeApproval(toolCall.Name, toolCall.Arguments));
                    _transcript.Add(new TranscriptEntry("approval", approver, $"Approval requested: {Pending.Summary}", DateTimeOffset.UtcNow));
                    yield return new DeskEvent("approval_required", new ApprovalData(Pending.RequestId, approver, toolCall.Name, args, Pending.Summary));
                    break;

                case WorkflowErrorEvent error:
                    yield return new DeskEvent("error", new ErrorData(Describe(error.Data)));
                    break;

                case ExecutorFailedEvent failed:
                    yield return new DeskEvent("error", new ErrorData($"{ResolveAgent(failed.ExecutorId) ?? failed.ExecutorId} failed: {Describe(failed.Data)}"));
                    break;
            }
        }

        if (Flush() is { } last) yield return last;

        yield return new DeskEvent("turn_end", new TurnEndData(
            ActiveAgent, Pending is null ? "customer" : "approval", [.. _path], Customer(), [.. Context.Audit]));
    }

    private CustomerSnapshot? Customer()
    {
        if (Context.Customer is not { } c) return null;
        return new CustomerSnapshot(
            c.Name, c.AccountId, Redaction.MaskPhone(c.Phone), c.Lines.Select(AccountTools.Describe).ToList(),
            c.Fiber is null ? null : Catalog.FindPlan(c.Fiber.PlanId)!.Name, c.Balance);
    }

    private void Enter()
    {
        if (!_gate.Wait(0))
        {
            throw new SessionBusyException("This conversation is already processing a turn.");
        }

        LastActivity = DateTimeOffset.UtcNow;
    }

    private void Exit()
    {
        LastActivity = DateTimeOffset.UtcNow;
        _gate.Release();
    }

    /// <summary>Maps executor ids ("billing_billing"), request port ids ("billing_billing_UserInput") and author names to agent keys.</summary>
    internal static string? ResolveAgent(string id) =>
        AgentRoster.All.Select(a => a.Key).FirstOrDefault(k => id == k || id.StartsWith(k + "_", StringComparison.Ordinal));

    private static bool IsSuccess(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.String) return !(result.GetString() ?? "").Contains("rejected", StringComparison.OrdinalIgnoreCase);
        if (result.ValueKind != JsonValueKind.Object) return true;
        if (result.TryGetProperty("error", out _)) return false;

        string[] flags = ["verified", "applied", "created", "booked", "added", "changed", "updated", "scheduled", "suspended"];
        return !flags.Any(f => result.TryGetProperty(f, out JsonElement v) && v.ValueKind == JsonValueKind.False);
    }

    private static JsonElement ToJson(object? value) => value switch
    {
        JsonElement e => e,
        null => JsonSerializer.SerializeToElement<string?>(null),
        _ => JsonSerializer.SerializeToElement(value, value.GetType(), AgentRoster.Json),
    };

    private static string Describe(object? error) => error switch
    {
        Exception { InnerException: { } inner } => inner.Message,
        Exception e => e.Message,
        null => "Unknown error",
        _ => error.ToString() ?? "Unknown error",
    };
}
