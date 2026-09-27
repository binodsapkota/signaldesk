using System.Text.Json;
using SignalDesk.Core.Tools;

namespace SignalDesk.Core.Runtime;

/// <summary>One server-sent event. <see cref="Type"/> becomes the SSE event name and <see cref="Data"/> its JSON payload.</summary>
public sealed record DeskEvent(string Type, object Data);

public sealed record TokenData(string Agent, string Text);

public sealed record MessageData(string Agent, string Text);

public sealed record HandoffData(string From, string To, string? Reason);

public sealed record ToolCallData(string Agent, string Tool, string CallId, IDictionary<string, object?> Args);

public sealed record ToolResultData(string Agent, string Tool, string CallId, bool Ok, JsonElement Result);

public sealed record ApprovalData(string RequestId, string Agent, string Tool, IDictionary<string, object?> Args, string Summary);

public sealed record ApprovalResolvedData(string RequestId, bool Approved);

public sealed record CustomerSnapshot(string Name, string AccountId, string Phone, IReadOnlyList<LineInfo> Lines, string? HomeInternet, decimal Balance);

public sealed record TurnEndData(
    string ActiveAgent, string Awaiting, IReadOnlyList<string> Path, CustomerSnapshot? Customer, IReadOnlyList<AuditEntry> Audit);

public sealed record ErrorData(string Message);

public sealed record TranscriptEntry(string Kind, string? Agent, string Text, DateTimeOffset At);

public sealed record PendingApproval(string RequestId, string Agent, string Tool, IDictionary<string, object?> Args, string Summary);

public sealed record SessionState(
    string Id, string Mode, string ActiveAgent, IReadOnlyList<string> Path, CustomerSnapshot? Customer,
    PendingApproval? PendingApproval, IReadOnlyList<TranscriptEntry> Transcript, IReadOnlyList<AuditEntry> Audit);
