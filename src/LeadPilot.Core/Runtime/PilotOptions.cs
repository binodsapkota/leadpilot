using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace LeadPilot.Core.Runtime;

public sealed class PilotOptions
{
    public const string Section = "LeadPilot";

    /// <summary>"demo" (offline deterministic agents) or "openai" (any OpenAI-compatible endpoint).</summary>
    public string Mode { get; set; } = "demo";

    public string? OpenAIApiKey { get; set; }

    public string OpenAIModel { get; set; } = "gpt-4.1-mini";

    /// <summary>Optional base URL for OpenAI-compatible servers such as Azure AI Foundry, Ollama or LM Studio.</summary>
    public string? OpenAIEndpoint { get; set; }

    /// <summary>Multiplier on simulated demo latency so parallel branches visibly overlap. Tests set this to 0.</summary>
    public double DemoPace { get; set; } = 1.0;

    /// <summary>"embedded" runs the CRM MCP server in-process; "http" connects to the standalone CRM server.</summary>
    public string CrmMode { get; set; } = "embedded";

    public string CrmEndpoint { get; set; } = "http://localhost:5090/mcp";

    /// <summary>Copywriter/reviewer attempts before the best draft goes to the rep anyway.</summary>
    public int MaxDrafts { get; set; } = 3;

    public int WorkspaceIdleMinutes { get; set; } = 30;

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
