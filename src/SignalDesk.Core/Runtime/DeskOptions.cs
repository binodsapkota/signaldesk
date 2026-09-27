using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace SignalDesk.Core.Runtime;

public sealed class DeskOptions
{
    public const string Section = "SignalDesk";

    /// <summary>"demo" (offline deterministic agents) or "openai" (any OpenAI-compatible endpoint).</summary>
    public string Mode { get; set; } = "demo";

    public string? OpenAIApiKey { get; set; }

    public string OpenAIModel { get; set; } = "gpt-4.1-mini";

    /// <summary>Optional base URL for OpenAI-compatible servers such as Azure AI Foundry, Ollama or LM Studio.</summary>
    public string? OpenAIEndpoint { get; set; }

    /// <summary>Delay between streamed demo tokens so the UI feels live. Tests set this to 0.</summary>
    public int DemoTokenDelayMs { get; set; } = 15;

    public int SessionIdleMinutes { get; set; } = 30;

    public int MaxHandoffsPerTurn { get; set; } = 6;

    public bool IsLive => Mode.Equals("openai", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(OpenAIApiKey);

    public string ModelLabel => IsLive ? OpenAIModel : "deterministic demo agents";

    public IChatClient? CreateLiveClient()
    {
        if (!IsLive) return null;

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(OpenAIEndpoint))
        {
            clientOptions.Endpoint = new Uri(OpenAIEndpoint);
        }

        return new OpenAIClient(new ApiKeyCredential(OpenAIApiKey!), clientOptions)
            .GetChatClient(OpenAIModel)
            .AsIChatClient();
    }
}
