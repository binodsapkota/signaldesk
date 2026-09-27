using System.Text.Json;
using SignalDesk.Core.Agents;
using SignalDesk.Core.Domain;
using SignalDesk.Core.Runtime;
using SignalDesk.Evals;

namespace SignalDesk.Tests;

/// <summary>End-to-end runs through the real Agent Framework handoff workflow, with deterministic demo agents.</summary>
public class WorkflowTests
{
    private static readonly DeskOptions Options = new() { DemoTokenDelayMs = 0 };

    public static TheoryData<string> GoldenCaseIds() =>
        new(GoldenRunner.Load(Path.Combine(AppContext.BaseDirectory, "golden.json")).Cases.Select(c => c.Id));

    [Theory]
    [MemberData(nameof(GoldenCaseIds))]
    public async Task Golden_conversation_passes(string id)
    {
        GoldenCase golden = GoldenRunner.Load(Path.Combine(AppContext.BaseDirectory, "golden.json")).Cases.Single(c => c.Id == id);

        CaseResult result = await GoldenRunner.RunCaseAsync(golden, Options);

        Assert.True(result.Passed, string.Join("; ", result.Problems));
    }

    [Fact]
    public async Task Specialist_to_specialist_handoff_carries_a_reason()
    {
        await using var runtime = new DeskRuntime(Options);
        DeskSession session = runtime.Create();

        List<DeskEvent> events = await Collect(session.SendAsync("My phone has no service. 312-555-0177 PIN 1937"));

        List<HandoffData> handoffs = events.Where(e => e.Type == "handoff").Select(e => (HandoffData)e.Data).ToList();
        Assert.Equal([(AgentRoster.Concierge, AgentRoster.TechSupport), (AgentRoster.TechSupport, AgentRoster.Billing)],
            handoffs.Select(h => (h.From, h.To)));
        Assert.Contains("non-payment", handoffs[1].Reason);
        Assert.Equal(AgentRoster.Billing, session.ActiveAgent);
    }

    [Fact]
    public async Task Next_message_returns_to_the_active_specialist()
    {
        await using var runtime = new DeskRuntime(Options);
        DeskSession session = runtime.Create();

        await Collect(session.SendAsync("I'm traveling to Japan for 10 days. 206-555-0123 PIN 5550"));
        List<DeskEvent> second = await Collect(session.SendAsync("Yes, add the 30-day pass."));

        Assert.DoesNotContain(second, e => e.Type == "handoff");
        Assert.Equal(AgentRoster.Plans, ((ApprovalData)second.Single(e => e.Type == "approval_required").Data).Agent);
    }

    [Fact]
    public async Task Pending_approval_blocks_new_messages_and_decline_changes_nothing()
    {
        await using var runtime = new DeskRuntime(Options);
        DeskSession session = runtime.Create();

        await Collect(session.SendAsync("My son's phone was stolen! 415-555-0142 PIN 4821"));
        PendingApproval pending = Assert.IsType<PendingApproval>(session.Pending);
        Assert.Equal("report_lost_device", pending.Tool);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(session.SendAsync("hello?")));

        List<DeskEvent> resolved = await Collect(session.ResolveApprovalAsync(pending.RequestId, approved: false));

        Assert.Null(session.Pending);
        Assert.Contains(resolved, e => e.Type == "tool_result" && !((ToolResultData)e.Data).Ok);
        Assert.All(session.Context.Customer!.Lines, l => Assert.Equal(LineStatus.Active, l.Status));
    }

    [Fact]
    public async Task Pins_never_leave_the_server_in_events()
    {
        await using var runtime = new DeskRuntime(Options);
        DeskSession session = runtime.Create();

        List<DeskEvent> events = await Collect(session.SendAsync("Why is my bill high? 415-555-0142 PIN 4821"));

        string wire = JsonSerializer.Serialize(events.Where(e => e.Type is "tool_call" or "tool_result").Select(e => e.Data));
        Assert.DoesNotContain("4821", wire);
        Assert.DoesNotContain("415-555-0142\",\"pin", wire);
    }

    [Fact]
    public async Task Tokens_stream_before_the_completed_message()
    {
        await using var runtime = new DeskRuntime(Options);
        DeskSession session = runtime.Create();

        List<DeskEvent> events = await Collect(session.SendAsync("Hi there"));

        int firstToken = events.FindIndex(e => e.Type == "token");
        int message = events.FindIndex(e => e.Type == "message");
        Assert.InRange(firstToken, 0, message - 1);
        Assert.Equal("turn_end", events[^1].Type);
    }

    private static async Task<List<DeskEvent>> Collect(IAsyncEnumerable<DeskEvent> stream)
    {
        List<DeskEvent> events = [];
        await foreach (DeskEvent e in stream)
        {
            events.Add(e);
        }

        return events;
    }
}
