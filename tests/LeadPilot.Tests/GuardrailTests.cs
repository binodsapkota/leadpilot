using System.Runtime.CompilerServices;
using System.Text.Json;
using LeadPilot.Core.Agents;
using LeadPilot.Core.Demo;
using LeadPilot.Core.Runtime;
using LeadPilot.Core.Workflow;
using Microsoft.Extensions.AI;

namespace LeadPilot.Tests;

/// <summary>
/// Swaps individual agents for misbehaving ones (as a real model might) and checks the workflow's code still
/// enforces policy, ownership and scoring rules.
/// </summary>
public class GuardrailTests
{
    [Theory]
    [InlineData("L-205", Decisions.Blocked, null)]
    [InlineData("L-202", Decisions.Pursue, "R-AR")]
    [InlineData("L-206", Decisions.Pursue, "R-LW")]
    [InlineData("L-204", Decisions.Disqualify, null)]
    [InlineData("L-203", Decisions.Nurture, "R-NQ")]
    public async Task Strategist_cannot_break_policy_or_ownership_rules(string leadId, string decision, string? owner)
    {
        var client = new RoutingClient(new() { [AgentRoster.Strategist] = Canned(new LeadPlan(
            "pursue", "R-JB", "Jordan Blake", "high", "Push hard", "Looks great", [], ["Call today"])) });
        await using PilotSession session = await PilotSession.CreateAsync("guard", new PilotOptions { DemoPace = 0 }, client);

        List<StreamEvent> events = await Collect(session.RunAsync(leadId));

        WorkflowTests.AssertNoErrors(events);
        LeadPlan plan = events.Approval().Plan;
        Assert.Equal(decision, plan.Decision);
        Assert.Equal(owner, plan.OwnerId);
        Assert.NotEmpty(plan.Overrides);
    }

    [Fact]
    public async Task Fit_scorer_cannot_inflate_the_score()
    {
        var client = new RoutingClient(new() { [AgentRoster.Qualifier] = Canned(new FitReport(99, "A", ["Trust me"])) });
        await using PilotSession session = await PilotSession.CreateAsync("guard", new PilotOptions { DemoPace = 0 }, client);

        List<StreamEvent> events = await Collect(session.RunAsync("L-204"));

        WorkflowTests.AssertNoErrors(events);
        Assert.Equal(10, events.Approval().Fit.Score);
        Assert.Equal(Decisions.Disqualify, events.Approval().Plan.Decision);
        Assert.Contains(events.Of<ToolCallData>(), c => c.Tool == "score_lead" && c.Caller == "code");
    }

    [Fact]
    public async Task Reviewer_cannot_wave_through_a_draft_the_policy_rejects()
    {
        var client = new RoutingClient(new() { [AgentRoster.Reviewer] = Canned(new { passed = true, issues = Array.Empty<object>(), summary = "LGTM" }) });
        await using PilotSession session = await PilotSession.CreateAsync("guard", new PilotOptions { DemoPace = 0 }, client);

        List<StreamEvent> events = await Collect(session.RunAsync("L-201"));

        WorkflowTests.AssertNoErrors(events);
        ReviewResult[] reviews = [.. events.Of<ReportData>().Where(r => r.Kind == "review").Select(r => (ReviewResult)r.Data)];
        Assert.False(reviews[0].Passed);
        Assert.Contains(reviews[0].Issues, i => i.Excerpt?.Contains("70%") == true);
        Assert.True(reviews[^1].Passed);
    }

    [Fact]
    public async Task Historian_cannot_invent_an_account()
    {
        var client = new RoutingClient(new()
        {
            [AgentRoster.Historian] = new ToolThenAnswer("find_account", new { domain = "harborline.com" },
                new HistoryReport("existing_customer", "A-999", "R-AR", "Aisha Rahman", "Big customer", [])),
        });
        await using PilotSession session = await PilotSession.CreateAsync("guard", new PilotOptions { DemoPace = 0 }, client);

        List<StreamEvent> events = await Collect(session.RunAsync("L-201"));

        WorkflowTests.AssertNoErrors(events);
        Assert.Equal("net_new", events.Report<HistoryReport>("history").Relationship);
        Assert.Equal("R-JB", events.Approval().Plan.OwnerId);
    }

    private static async Task<List<StreamEvent>> Collect(IAsyncEnumerable<StreamEvent> source)
    {
        var list = new List<StreamEvent>();
        await foreach (StreamEvent e in source) list.Add(e);
        return list;
    }

    private static IChatClient Canned(object answer) => new ToolThenAnswer(null, null, answer);

    /// <summary>Routes each agent's calls to an override if present, otherwise to its normal demo brain.</summary>
    private sealed class RoutingClient(Dictionary<string, IChatClient> overrides) : IChatClient
    {
        private readonly Dictionary<string, IChatClient> _clients = new(overrides);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            For(messages, options).GetResponseAsync(messages, options, cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            For(messages, options).GetStreamingResponseAsync(messages, options, cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private IChatClient For(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            string text = (options?.Instructions ?? "") + string.Concat(messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
            AgentSpec spec = AgentRoster.All.Single(s => text.Contains(s.Instructions, StringComparison.Ordinal));
            lock (_clients)
            {
                if (!_clients.TryGetValue(spec.Key, out IChatClient? client)) _clients[spec.Key] = client = new DemoChatClient(spec.Key, 0);
                return client;
            }
        }
    }

    /// <summary>Optionally calls one tool, then returns a fixed JSON answer regardless of what the tool said.</summary>
    private sealed class ToolThenAnswer(string? tool, object? args, object answer) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            bool called = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any();
            if (tool is not null && !called)
            {
                var callArgs = JsonSerializer.SerializeToElement(args, PilotJson.Options).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call_1", tool, callArgs)]);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, JsonSerializer.Serialize(answer, PilotJson.Options));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
