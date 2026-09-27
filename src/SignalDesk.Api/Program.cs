using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SignalDesk.Core;
using SignalDesk.Core.Agents;
using SignalDesk.Core.Runtime;

var builder = WebApplication.CreateBuilder(args);

DeskOptions desk = builder.Configuration.GetSection(DeskOptions.Section).Get<DeskOptions>() ?? new DeskOptions();
desk.OpenAIApiKey ??= builder.Configuration["OPENAI_API_KEY"];
builder.Services.AddSingleton(desk);
builder.Services.AddSingleton<DeskRuntime>();

string[] origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("turns", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseCors();
app.UseRateLimiter();

var api = app.MapGroup("/api");

api.MapGet("/health", (DeskRuntime runtime) => new
{
    status = "ok",
    mode = desk.IsLive ? "openai" : "demo",
    model = desk.ModelLabel,
    sessions = runtime.Count,
});

api.MapGet("/agents", () => AgentRoster.Describe());

api.MapGet("/scenarios", () => new { scenarios = Scenarios.All, credentials = Scenarios.Credentials });

api.MapPost("/sessions", (DeskRuntime runtime) =>
{
    DeskSession session = runtime.Create();
    return TypedResults.Created($"/api/sessions/{session.Id}", session.Snapshot());
}).RequireRateLimiting("turns");

api.MapGet("/sessions/{id}", IResult (string id, DeskRuntime runtime) =>
    runtime.Get(id) is { } session ? TypedResults.Ok(session.Snapshot()) : TypedResults.NotFound());

api.MapPost("/sessions/{id}/messages", IResult (string id, MessageRequest body, DeskRuntime runtime, CancellationToken ct) =>
{
    if (runtime.Get(id) is not { } session) return TypedResults.NotFound();

    string text = body.Text?.Trim() ?? "";
    if (text.Length is 0 or > 1000) return TypedResults.BadRequest(new { error = "Message must be 1-1000 characters." });
    if (session.IsBusy) return TypedResults.Conflict(new { error = "This conversation is already processing a turn." });
    if (session.Pending is not null) return TypedResults.Conflict(new { error = "Approve or decline the pending action first." });

    return TypedResults.ServerSentEvents(ToSse(session.SendAsync(text, ct), ct));
}).RequireRateLimiting("turns");

api.MapPost("/sessions/{id}/approval", IResult (string id, ApprovalRequest body, DeskRuntime runtime, CancellationToken ct) =>
{
    if (runtime.Get(id) is not { } session) return TypedResults.NotFound();
    if (session.IsBusy) return TypedResults.Conflict(new { error = "This conversation is already processing a turn." });
    if (session.Pending?.RequestId != body.RequestId) return TypedResults.Conflict(new { error = "No pending approval with that id." });

    return TypedResults.ServerSentEvents(ToSse(session.ResolveApprovalAsync(body.RequestId, body.Approved, ct), ct));
}).RequireRateLimiting("turns");

app.Run();

static async IAsyncEnumerable<SseItem<object>> ToSse(IAsyncEnumerable<DeskEvent> events, [EnumeratorCancellation] CancellationToken ct)
{
    await using IAsyncEnumerator<DeskEvent> e = events.GetAsyncEnumerator(ct);
    while (true)
    {
        DeskEvent? current = null;
        string? failure = null;
        try
        {
            if (!await e.MoveNextAsync()) break;
            current = e.Current;
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        if (failure is not null)
        {
            yield return new SseItem<object>(new ErrorData(failure), "error");
            yield break;
        }

        yield return new SseItem<object>(current!.Data, current.Type);
    }
}

public sealed record MessageRequest(string? Text);

public sealed record ApprovalRequest(string RequestId, bool Approved);

public partial class Program;
