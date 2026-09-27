using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Workflow;

/// <summary>Tool results captured during an agent run, so executors can enforce them over the model's summary.</summary>
public sealed class ToolCapture
{
    private readonly Dictionary<string, JsonElement> _results = [];

    public void Set(string tool, JsonElement result) => _results[tool] = result;

    public bool TryGet<T>(string tool, out T value)
    {
        value = default!;
        if (!_results.TryGetValue(tool, out JsonElement raw) || raw.ValueKind != JsonValueKind.Object) return false;
        try
        {
            value = raw.Deserialize<T>(PilotJson.Options)!;
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Shared plumbing for executors: node lifecycle events, agent runs, and direct MCP calls.</summary>
public static class Steps
{
    public const string ContextMarker = "CONTEXT (JSON):";

    public static string Prompt(string task, object context) =>
        $"{task}\n\n{ContextMarker}\n{JsonSerializer.Serialize(context, PilotJson.Options)}";

    public static ValueTask StartAsync(IWorkflowContext ctx, string node, int attempt = 1, CancellationToken ct = default) =>
        ctx.AddEventAsync(new PilotEvent("node_started", new NodeStartedData(node, attempt)), ct);

    public static ValueTask CompleteAsync(IWorkflowContext ctx, string node, Stopwatch sw, string summary, CancellationToken ct = default) =>
        ctx.AddEventAsync(new PilotEvent("node_completed", new NodeCompletedData(node, sw.ElapsedMilliseconds, summary)), ct);

    public static ValueTask ReportAsync(IWorkflowContext ctx, string node, string kind, object data, CancellationToken ct = default) =>
        ctx.AddEventAsync(new PilotEvent("report", new ReportData(node, kind, data)), ct);

    /// <summary>Runs an agent, streaming its tool calls as events, and returns its final text.</summary>
    public static async Task<string> RunAgentAsync(
        AIAgent agent,
        string node,
        string prompt,
        IWorkflowContext ctx,
        ToolCapture? capture = null,
        ChatResponseFormat? format = null,
        int tokenVersion = 0,
        CancellationToken ct = default)
    {
        var options = new ChatClientAgentRunOptions(new ChatOptions { ResponseFormat = format });
        var text = new StringBuilder();
        var calls = new Dictionary<string, (string Tool, long Started)>();
        var clock = Stopwatch.StartNew();

        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(prompt, null, options, ct))
        {
            foreach (AIContent content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        calls[call.CallId] = (call.Name, clock.ElapsedMilliseconds);
                        await ctx.AddEventAsync(new PilotEvent("tool_call", new ToolCallData(
                            node, call.Name, call.CallId, JsonSerializer.SerializeToElement(call.Arguments, PilotJson.Options), "agent")), ct);
                        break;

                    case FunctionResultContent result when calls.TryGetValue(result.CallId, out var call):
                        JsonElement json = ToJson(result.Result);
                        bool ok = result.Exception is null && !IsError(json);
                        if (ok) capture?.Set(call.Tool, json);
                        else json = AsError(json);
                        await ctx.AddEventAsync(new PilotEvent("tool_result", new ToolResultData(
                            node, call.Tool, result.CallId, ok, json, clock.ElapsedMilliseconds - call.Started)), ct);
                        break;

                    case TextContent { Text.Length: > 0 } token:
                        text.Append(token.Text);
                        if (tokenVersion > 0)
                        {
                            await ctx.AddEventAsync(new PilotEvent("token", new TokenData(node, tokenVersion, token.Text)), ct);
                        }
                        break;
                }
            }
        }

        return text.ToString();
    }

    public static async Task<T> RunAgentJsonAsync<T>(AIAgent agent, string node, string prompt, IWorkflowContext ctx, ToolCapture? capture = null, CancellationToken ct = default)
    {
        string text = await RunAgentAsync(agent, node, prompt, ctx, capture, ChatResponseFormat.ForJsonSchema<T>(PilotJson.Options), ct: ct);
        return ParseJson<T>(text, agent.Name ?? node);
    }

    /// <summary>Calls a CRM tool from code (not via a model), with the same trace events an agent call produces.</summary>
    public static async Task<T> CallCrmAsync<T>(LeadRun run, IWorkflowContext ctx, string node, string tool, Dictionary<string, object?> args, CancellationToken ct = default)
    {
        string callId = $"code-{Guid.NewGuid():N}"[..13];
        var clock = Stopwatch.StartNew();
        await ctx.AddEventAsync(new PilotEvent("tool_call", new ToolCallData(node, tool, callId, JsonSerializer.SerializeToElement(args, PilotJson.Options), "code")), ct);
        try
        {
            (T value, JsonElement raw) = await run.Crm.CallAsync<T>(tool, args, ct);
            await ctx.AddEventAsync(new PilotEvent("tool_result", new ToolResultData(node, tool, callId, true, raw, clock.ElapsedMilliseconds)), ct);
            return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ctx.AddEventAsync(new PilotEvent("tool_result", new ToolResultData(
                node, tool, callId, false, JsonSerializer.SerializeToElement(new { error = ex.Message }, PilotJson.Options), clock.ElapsedMilliseconds)), ct);
            throw;
        }
    }

    public static T ParseJson<T>(string text, string who)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException($"{who} did not return JSON: {Clip(text)}");
        try
        {
            return JsonSerializer.Deserialize<T>(text[start..(end + 1)], PilotJson.Options)
                ?? throw new InvalidOperationException($"{who} returned empty JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{who} returned malformed JSON ({ex.Message}): {Clip(text)}");
        }
    }

    public static JsonElement ToJson(object? value) => value switch
    {
        JsonElement e => e,
        null => JsonSerializer.SerializeToElement<object?>(null),
        string s => TryParse(s),
        TextContent t => TryParse(t.Text),
        IEnumerable<AIContent> contents => TryParse(string.Concat(contents.OfType<TextContent>().Select(t => t.Text))),
        _ => JsonSerializer.SerializeToElement(value, value.GetType(), PilotJson.Options),
    };

    /// <summary>Turns an MCP error result into a compact <c>{ "error": "..." }</c> for the trace.</summary>
    private static JsonElement AsError(JsonElement json)
    {
        string message = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array
            ? string.Concat(content.EnumerateArray().Select(c => c.TryGetProperty("text", out JsonElement t) ? t.GetString() : ""))
            : json.ToString();
        return JsonSerializer.SerializeToElement(new { error = Mcp.CrmConnection.CleanError(message) }, PilotJson.Options);
    }

    private static JsonElement TryParse(string s)
    {
        try
        {
            return JsonDocument.Parse(s).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(s);
        }
    }

    private static bool IsError(JsonElement json) =>
        (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("isError", out JsonElement e) && e.ValueKind == JsonValueKind.True) ||
        (json.ValueKind == JsonValueKind.String && json.GetString()!.StartsWith("Error", StringComparison.OrdinalIgnoreCase));

    private static string Clip(string s) => s.Length > 200 ? s[..200] + "..." : s;
}
