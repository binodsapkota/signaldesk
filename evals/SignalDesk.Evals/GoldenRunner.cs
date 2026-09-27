using System.Text.Json;
using SignalDesk.Core.Runtime;

namespace SignalDesk.Evals;

public sealed record GoldenCase(
    string Id,
    string Category,
    IReadOnlyList<string> Turns,
    IReadOnlyList<string> Path,
    IReadOnlyList<string>? Tools = null,
    IReadOnlyList<string>? ForbiddenTools = null,
    IReadOnlyList<string>? Audit = null,
    IReadOnlyList<string>? ForbiddenAudit = null,
    IReadOnlyList<string>? AuditText = null);

public sealed record GoldenSet(IReadOnlyList<GoldenCase> Cases);

public sealed record CaseResult(
    GoldenCase Case,
    IReadOnlyList<string> Path,
    IReadOnlyList<string> ToolsCalled,
    IReadOnlyList<string> AuditActions,
    bool RoutingOk,
    double ToolRecall,
    bool GuardrailsOk,
    bool TaskOk,
    IReadOnlyList<string> Problems)
{
    public bool Passed => RoutingOk && ToolRecall >= 1.0 && GuardrailsOk && TaskOk;
}

public sealed record EvalSummary(IReadOnlyList<CaseResult> Results)
{
    public double RoutingAccuracy => Results.Average(r => r.RoutingOk ? 1.0 : 0.0);
    public double ToolRecall => Results.Average(r => r.ToolRecall);
    public double GuardrailAdherence => Results.Average(r => r.GuardrailsOk ? 1.0 : 0.0);
    public double TaskSuccess => Results.Average(r => r.TaskOk ? 1.0 : 0.0);
}

/// <summary>Drives scripted conversations through the real workflow and scores routing, tool use and guardrails.</summary>
public static class GoldenRunner
{
    public const string Approve = "[approve]";
    public const string Decline = "[decline]";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static GoldenSet Load(string path) =>
        JsonSerializer.Deserialize<GoldenSet>(File.ReadAllText(path), Json) ?? throw new InvalidOperationException("Empty golden set.");

    public static async Task<EvalSummary> RunAsync(IEnumerable<GoldenCase> cases, DeskOptions options, CancellationToken ct = default)
    {
        List<CaseResult> results = [];
        foreach (GoldenCase c in cases)
        {
            results.Add(await RunCaseAsync(c, options, ct));
        }

        return new EvalSummary(results);
    }

    public static async Task<CaseResult> RunCaseAsync(GoldenCase c, DeskOptions options, CancellationToken ct = default, Action<string>? log = null)
    {
        await using var runtime = new DeskRuntime(options);
        DeskSession session = runtime.Create();
        var events = new TracingList(log);
        List<string> problems = [];

        foreach (string turn in c.Turns)
        {
            log?.Invoke($"\n>>> {turn}");
            if (turn is Approve or Decline)
            {
                if (session.Pending is not { } pending)
                {
                    problems.Add($"expected an approval before '{turn}'");
                    continue;
                }

                await foreach (DeskEvent e in session.ResolveApprovalAsync(pending.RequestId, turn == Approve, ct))
                {
                    events.Add(e);
                }

                continue;
            }

            if (session.Pending is { } unexpected)
            {
                problems.Add($"unexpected approval for {unexpected.Tool}; declined");
                await foreach (DeskEvent e in session.ResolveApprovalAsync(unexpected.RequestId, false, ct))
                {
                    events.Add(e);
                }
            }

            await foreach (DeskEvent e in session.SendAsync(turn, ct))
            {
                events.Add(e);
            }
        }

        problems.AddRange(events.Where(e => e.Type == "error").Select(e => "error: " + ((ErrorData)e.Data).Message));

        SessionState state = session.Snapshot();
        List<string> tools = events.Where(e => e.Type == "tool_call").Select(e => ((ToolCallData)e.Data).Tool).ToList();
        List<string> audit = state.Audit.Select(a => a.Action).ToList();

        bool routingOk = state.Path.SequenceEqual(c.Path);
        if (!routingOk) problems.Add($"path {string.Join(">", state.Path)} != {string.Join(">", c.Path)}");

        IReadOnlyList<string> expectedTools = c.Tools ?? [];
        double recall = expectedTools.Count == 0 ? 1.0 : expectedTools.Count(tools.Contains) / (double)expectedTools.Count;
        foreach (string missing in expectedTools.Where(t => !tools.Contains(t))) problems.Add($"missing tool {missing}");

        List<string> violations =
        [
            .. (c.ForbiddenTools ?? []).Where(tools.Contains).Select(t => $"forbidden tool {t}"),
            .. (c.ForbiddenAudit ?? []).Where(audit.Contains).Select(a => $"forbidden change {a}"),
        ];
        problems.AddRange(violations);

        List<string> taskGaps =
        [
            .. (c.Audit ?? []).Where(a => !audit.Contains(a)).Select(a => $"missing change {a}"),
            .. (c.AuditText ?? []).Where(x => !state.Audit.Any(a => a.Detail.Contains(x, StringComparison.OrdinalIgnoreCase))).Select(x => $"no audit entry mentions '{x}'"),
        ];
        problems.AddRange(taskGaps);
        bool taskOk = taskGaps.Count == 0 && !events.Any(e => e.Type == "error");

        return new CaseResult(c, state.Path, tools, audit, routingOk, recall, violations.Count == 0, taskOk, problems);
    }

    private sealed class TracingList(Action<string>? log) : List<DeskEvent>
    {
        public new void Add(DeskEvent e)
        {
            base.Add(e);
            if (log is not null && e.Type != "token")
            {
                log($"  [{e.Type}] {JsonSerializer.Serialize(e.Data, Json)}");
            }
        }
    }
}
