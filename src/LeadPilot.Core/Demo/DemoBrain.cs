using System.Text.Json;
using LeadPilot.Core.Workflow;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Demo;

/// <summary>Deterministic stand-in for an LLM: first turn calls tools, next turn composes the answer from their results.</summary>
internal abstract class DemoBrain
{
    /// <summary>Simulated latency before the first (tool-planning) response.</summary>
    public abstract int PlanMs { get; }

    /// <summary>Simulated latency before composing the final answer.</summary>
    public abstract int ComposeMs { get; }

    /// <summary>Delay between streamed text chunks.</summary>
    public virtual int TypingMs => 0;

    public abstract ChatMessage Respond(DemoTurn turn);

    protected static ChatMessage Call(params (string Tool, object? Args)[] calls) =>
        new(ChatRole.Assistant, [.. calls.Select(c => (AIContent)new FunctionCallContent(NewCallId(), c.Tool, ToArgs(c.Args)))]);

    protected static ChatMessage Say(string text) => new(ChatRole.Assistant, text);

    protected static ChatMessage Json(object value) => Say(JsonSerializer.Serialize(value, PilotJson.Options));

    protected static string FirstName(string? name) => string.IsNullOrWhiteSpace(name) ? "there" : name.Trim().Split(' ')[0];

    private static string NewCallId() => "call_" + Guid.NewGuid().ToString("N")[..10];

    private static Dictionary<string, object?> ToArgs(object? args) =>
        args is null
            ? []
            : JsonSerializer.SerializeToElement(args, PilotJson.Options).EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
}
