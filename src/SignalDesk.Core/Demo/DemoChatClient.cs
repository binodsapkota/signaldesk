using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using SignalDesk.Core.Agents;
using SignalDesk.Core.Demo.Brains;

namespace SignalDesk.Core.Demo;

/// <summary>
/// An <see cref="IChatClient"/> backed by a deterministic brain, so the whole multi-agent workflow runs offline.
/// Text is streamed word-by-word to mimic token streaming from a hosted model.
/// </summary>
public sealed class DemoChatClient : IChatClient
{
    private readonly DemoBrain _brain;
    private readonly TimeSpan _tokenDelay;

    public DemoChatClient(string agentKey, int tokenDelayMs = 0)
    {
        _brain = agentKey switch
        {
            AgentRoster.Concierge => new ConciergeBrain(),
            AgentRoster.TechSupport => new TechSupportBrain(),
            AgentRoster.Billing => new BillingBrain(),
            AgentRoster.Account => new AccountBrain(),
            AgentRoster.Plans => new PlansBrain(),
            AgentRoster.Retention => new RetentionBrain(),
            _ => throw new ArgumentOutOfRangeException(nameof(agentKey), agentKey, "No demo brain for this agent."),
        };
        _tokenDelay = TimeSpan.FromMilliseconds(tokenDelayMs);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatMessage reply = _brain.Respond(new Transcript(messages), options);
        return Task.FromResult(new ChatResponse(reply) { ModelId = "signaldesk-demo" });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessage reply = _brain.Respond(new Transcript(messages), options);
        string messageId = "msg_" + Guid.NewGuid().ToString("N")[..12];

        foreach (AIContent content in reply.Contents)
        {
            if (content is TextContent text)
            {
                foreach (string chunk in Chunk(text.Text))
                {
                    if (_tokenDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(_tokenDelay, cancellationToken);
                    }

                    yield return new ChatResponseUpdate(ChatRole.Assistant, chunk) { MessageId = messageId, ModelId = "signaldesk-demo" };
                }
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [content]) { MessageId = messageId, ModelId = "signaldesk-demo" };
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static IEnumerable<string> Chunk(string text)
    {
        int start = 0;
        int words = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is ' ' or '\n' && ++words % 3 == 0)
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }
}
