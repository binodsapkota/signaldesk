using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SignalDesk.Tests;

public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.UseSetting("SignalDesk:DemoTokenDelayMs", "0"))
        .CreateClient();

    [Fact]
    public async Task Health_reports_demo_mode()
    {
        JsonElement health = await _client.GetFromJsonAsync<JsonElement>("/api/health");

        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal("demo", health.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Agents_expose_tools_and_approval_flags()
    {
        JsonElement agents = await _client.GetFromJsonAsync<JsonElement>("/api/agents");

        Assert.Equal(6, agents.GetArrayLength());
        JsonElement billing = agents.EnumerateArray().Single(a => a.GetProperty("key").GetString() == "billing");
        JsonElement credit = billing.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "apply_courtesy_credit");
        Assert.True(credit.GetProperty("requiresApproval").GetBoolean());
    }

    [Fact]
    public async Task Conversation_streams_handoffs_and_gates_on_approval()
    {
        string id = await CreateSession();

        string stream = await Send(id, "My son's phone was stolen! 415-555-0142 PIN 4821");

        Assert.Contains("event: handoff", stream);
        Assert.Contains("event: approval_required", stream);
        Assert.Contains("\"awaiting\":\"approval\"", stream);

        HttpResponseMessage blocked = await _client.PostAsJsonAsync($"/api/sessions/{id}/messages", new { text = "hello" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        JsonElement state = await _client.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}");
        string requestId = state.GetProperty("pendingApproval").GetProperty("requestId").GetString()!;

        HttpResponseMessage approved = await _client.PostAsJsonAsync($"/api/sessions/{id}/approval", new { requestId, approved = true });
        string approvalStream = await approved.Content.ReadAsStringAsync();

        Assert.Contains("event: approval_resolved", approvalStream);
        Assert.Contains("line_suspended", approvalStream);
    }

    [Fact]
    public async Task Unknown_session_and_bad_input_are_rejected()
    {
        HttpResponseMessage missing = await _client.PostAsJsonAsync("/api/sessions/nope/messages", new { text = "hi" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        string id = await CreateSession();
        HttpResponseMessage empty = await _client.PostAsJsonAsync($"/api/sessions/{id}/messages", new { text = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    private async Task<string> CreateSession()
    {
        HttpResponseMessage created = await _client.PostAsync("/api/sessions", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task<string> Send(string id, string text)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync($"/api/sessions/{id}/messages", new { text });
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }
}
