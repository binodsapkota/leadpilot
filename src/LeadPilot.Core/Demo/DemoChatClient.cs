using System.Runtime.CompilerServices;
using System.Text.Json;
using LeadPilot.Core.Agents;
using LeadPilot.Core.Demo.Brains;
using LeadPilot.Core.Workflow;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Demo;

/// <summary>
/// An <see cref="IChatClient"/> backed by a deterministic brain, so the whole workflow runs offline with no API key.
/// Brains return exactly what a model would (tool calls, then text); the Agent Framework's function-invocation loop
/// executes the calls against the real MCP server.
/// </summary>
public sealed class DemoChatClient : IChatClient
{
    private readonly DemoBrain _brain;
    private readonly double _pace;

    /// <param name="agentKey">Which agent this client stands in for.</param>
    /// <param name="pace">Multiplier on simulated think and typing time; 0 disables delays (tests).</param>
    public DemoChatClient(string agentKey, double pace = 1.0)
    {
        _brain = agentKey switch
        {
            AgentRoster.Researcher => new ResearcherBrain(),
            AgentRoster.Historian => new HistorianBrain(),
            AgentRoster.Qualifier => new FitBrain(),
            AgentRoster.Strategist => new StrategistBrain(),
            AgentRoster.Copywriter => new CopywriterBrain(),
            AgentRoster.Reviewer => new ReviewerBrain(),
            _ => throw new ArgumentOutOfRangeException(nameof(agentKey), agentKey, "No demo brain for this agent."),
        };
        _pace = Math.Max(0, pace);
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var turn = new DemoTurn(messages);
        await Think(turn, cancellationToken);
        return new ChatResponse(_brain.Respond(turn)) { ModelId = "leadpilot-demo" };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var turn = new DemoTurn(messages);
        await Think(turn, cancellationToken);
        ChatMessage reply = _brain.Respond(turn);
        string messageId = "msg_" + Guid.NewGuid().ToString("N")[..12];
        TimeSpan typing = TimeSpan.FromMilliseconds(_brain.TypingMs * _pace);

        foreach (AIContent content in reply.Contents)
        {
            if (content is TextContent text)
            {
                foreach (string chunk in Chunk(text.Text))
                {
                    if (typing > TimeSpan.Zero) await Task.Delay(typing, cancellationToken);
                    yield return new ChatResponseUpdate(ChatRole.Assistant, chunk) { MessageId = messageId, ModelId = "leadpilot-demo" };
                }
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [content]) { MessageId = messageId, ModelId = "leadpilot-demo" };
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private Task Think(DemoTurn turn, CancellationToken ct)
    {
        int ms = (int)((turn.HasResults ? _brain.ComposeMs : _brain.PlanMs) * _pace);
        return ms > 0 ? Task.Delay(ms, ct) : Task.CompletedTask;
    }

    private static IEnumerable<string> Chunk(string text)
    {
        int start = 0;
        int words = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is ' ' or '\n' && ++words % 2 == 0)
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }

        if (start < text.Length) yield return text[start..];
    }
}

/// <summary>What a brain can see: the task, the JSON context the executor passed, and tool results so far.</summary>
internal sealed class DemoTurn
{
    private readonly Dictionary<string, JsonElement> _results = [];

    public DemoTurn(IEnumerable<ChatMessage> messages)
    {
        List<ChatMessage> list = [.. messages];
        string prompt = list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        int at = prompt.IndexOf(Steps.ContextMarker, StringComparison.Ordinal);
        Task = at < 0 ? prompt.Trim() : prompt[..at].Trim();
        Context = at < 0 ? JsonDocument.Parse("{}").RootElement : JsonDocument.Parse(prompt[(at + Steps.ContextMarker.Length)..]).RootElement.Clone();

        var names = new Dictionary<string, string>();
        foreach (AIContent content in list.SelectMany(m => m.Contents))
        {
            if (content is FunctionCallContent call) names[call.CallId] = call.Name;
            else if (content is FunctionResultContent result && names.TryGetValue(result.CallId, out string? name)) _results[name] = Steps.ToJson(result.Result);
        }
    }

    public string Task { get; }

    public JsonElement Context { get; }

    public bool HasResults => _results.Count > 0;

    public bool Has(string tool) => _results.ContainsKey(tool);

    public T Result<T>(string tool) => _results[tool].Deserialize<T>(PilotJson.Options)!;

    public T Ctx<T>(string property) =>
        Context.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? value.Deserialize<T>(PilotJson.Options)! : default!;
}
