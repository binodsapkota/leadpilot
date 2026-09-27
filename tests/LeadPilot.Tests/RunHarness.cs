using System.Text.Json;
using LeadPilot.Core.Runtime;
using LeadPilot.Core.Workflow;

namespace LeadPilot.Tests;

/// <summary>Drives a workspace through a lead run in demo mode with no artificial latency.</summary>
internal sealed class RunHarness : IAsyncDisposable
{
    private RunHarness(PilotSession session) => Session = session;

    public PilotSession Session { get; }

    public static async Task<RunHarness> CreateAsync(int maxDrafts = 3) =>
        new(await PilotSession.CreateAsync("test", new PilotOptions { DemoPace = 0, MaxDrafts = maxDrafts }, null));

    public async Task<List<StreamEvent>> RunAsync(string leadId) => await Collect(Session.RunAsync(leadId));

    public async Task<List<StreamEvent>> DecideAsync(ApprovalDecision decision)
    {
        PendingApproval pending = Session.Pending ?? throw new InvalidOperationException("Nothing is waiting for approval.");
        return await Collect(Session.DecideAsync(pending.RunId, decision));
    }

    public ValueTask DisposeAsync() => Session.DisposeAsync();

    private static async Task<List<StreamEvent>> Collect(IAsyncEnumerable<StreamEvent> events)
    {
        var list = new List<StreamEvent>();
        await foreach (StreamEvent e in events) list.Add(e);
        return list;
    }
}

internal static class EventExtensions
{
    public static IEnumerable<T> Of<T>(this IEnumerable<StreamEvent> events) => events.Select(e => e.Data).OfType<T>();

    public static T Report<T>(this IEnumerable<StreamEvent> events, string kind) =>
        events.Of<ReportData>().Last(r => r.Kind == kind).Data is T typed
            ? typed
            : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(events.Of<ReportData>().Last(r => r.Kind == kind).Data, PilotJson.Options), PilotJson.Options)!;

    public static ApprovalRequest Approval(this IEnumerable<StreamEvent> events) => events.Of<ApprovalRequiredData>().Single().Request;

    public static string[] Errors(this IEnumerable<StreamEvent> events) => [.. events.Of<ErrorData>().Select(e => e.Message)];
}
