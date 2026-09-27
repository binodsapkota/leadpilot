using System.Diagnostics;
using System.Runtime.CompilerServices;
using LeadPilot.Core.Agents;
using LeadPilot.Core.Demo;
using LeadPilot.Core.Mcp;
using LeadPilot.Core.Workflow;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Runtime;

/// <summary>An event streamed to the UI.</summary>
public sealed record StreamEvent(string Type, object Data);

public sealed record RunStartedData(string RunId, string LeadId, string Mode, string Transport);

public sealed record PendingApproval(string RunId, string RequestId, ApprovalRequest Request);

public sealed record WorkspaceSnapshot(string Id, string Mode, string Transport, LeadSummary[] Leads, CrmChange[] Changes, PendingApproval? Pending);

public sealed record NewLead(string Name, string Title, string Email, string Company, string Country, string Source, string? Message, bool MarketingConsent);

/// <summary>
/// One browser workspace: its own MCP session (and therefore its own CRM sandbox), its agents, and at most one
/// lead run in flight. A run pauses at the approval port and resumes when the rep decides.
/// </summary>
public sealed class PilotSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IReadOnlyDictionary<string, AIAgent> _agents;
    private ActiveRun? _active;

    private PilotSession(string id, PilotOptions options, CrmConnection crm, IReadOnlyDictionary<string, AIAgent> agents)
    {
        Id = id;
        Options = options;
        Crm = crm;
        _agents = agents;
    }

    public string Id { get; }

    public PilotOptions Options { get; }

    public CrmConnection Crm { get; }

    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsBusy => _gate.CurrentCount == 0;

    public PendingApproval? Pending =>
        _active is { Pending: { } pending } active ? new PendingApproval(active.Id, pending.RequestId, active.PendingRequest!) : null;

    public static async Task<PilotSession> CreateAsync(string id, PilotOptions options, IChatClient? liveClient, CancellationToken ct = default) =>
        Create(id, options, liveClient, await CrmConnection.ConnectAsync(options.CrmMode, options.CrmEndpoint, ct: ct));

    public static PilotSession Create(string id, PilotOptions options, IChatClient? liveClient, CrmConnection crm)
    {
        IReadOnlyDictionary<string, AIAgent> agents = AgentFactory.Create(crm, spec => liveClient ?? new DemoChatClient(spec.Key, options.DemoPace));
        return new PilotSession(id, options, crm, agents);
    }

    public async Task<WorkspaceSnapshot> SnapshotAsync(CancellationToken ct = default)
    {
        Touch();
        (LeadSummary[] leads, _) = await Crm.CallAsync<LeadSummary[]>("list_inbound_leads", new Dictionary<string, object?>(), ct);
        (CrmChange[] changes, _) = await Crm.CallAsync<CrmChange[]>("get_crm_changes", new Dictionary<string, object?>(), ct);
        return new WorkspaceSnapshot(Id, Options.IsLive ? "openai" : "demo", Crm.Transport, leads, changes, Pending);
    }

    public async Task<LeadSummary> AddLeadAsync(NewLead lead, CancellationToken ct = default)
    {
        Touch();
        (LeadSummary created, _) = await Crm.CallAsync<LeadSummary>("add_inbound_lead", new Dictionary<string, object?>
        {
            ["name"] = lead.Name,
            ["title"] = lead.Title,
            ["email"] = lead.Email,
            ["company"] = lead.Company,
            ["country"] = lead.Country,
            ["source"] = lead.Source,
            ["message"] = lead.Message ?? "",
            ["marketingConsent"] = lead.MarketingConsent,
        }, ct);
        return created;
    }

    /// <summary>Runs the workflow for one lead until it finishes or pauses for approval. Replaces any paused run.</summary>
    public async IAsyncEnumerable<StreamEvent> RunAsync(string leadId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new InvalidOperationException("This workspace is already running a lead.");
        try
        {
            Touch();
            await AbandonActiveAsync();

            var run = new LeadRun(Crm, _agents, Options.MaxDrafts);
            string runId = "run_" + Guid.NewGuid().ToString("N")[..10];
            StreamingRun streaming = await InProcessExecution.Default.RunStreamingAsync(LeadWorkflowFactory.Build(run), new LeadRequest(leadId), runId, ct);
            _active = new ActiveRun(runId, run, streaming);

            yield return new StreamEvent("run_started", new RunStartedData(runId, leadId, Options.IsLive ? "openai" : "demo", Crm.Transport));
            await foreach (StreamEvent e in PumpAsync(_active, ct)) yield return e;
        }
        finally
        {
            await SettleAsync();
            Touch();
            _gate.Release();
        }
    }

    /// <summary>Delivers the rep's decision to the paused run and streams the rest of it.</summary>
    public async IAsyncEnumerable<StreamEvent> DecideAsync(string runId, ApprovalDecision decision, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new InvalidOperationException("This workspace is busy.");
        try
        {
            Touch();
            if (_active is not { Pending: { } pending } active || active.Id != runId)
            {
                throw new InvalidOperationException("No run is waiting for approval with that id.");
            }

            yield return new StreamEvent("node_completed", new NodeCompletedData(
                Nodes.Approval, active.WaitClock.ElapsedMilliseconds, decision.Approved ? "Approved by rep" : "Rejected by rep"));

            active.Pending = null;
            await active.Streaming.SendResponseAsync(pending.CreateResponse(decision));
            await foreach (StreamEvent e in PumpAsync(active, ct)) yield return e;
        }
        finally
        {
            await SettleAsync();
            Touch();
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await AbandonActiveAsync();
        await Crm.DisposeAsync();
        _gate.Dispose();
    }

    private static async IAsyncEnumerable<StreamEvent> PumpAsync(ActiveRun active, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (WorkflowEvent evt in active.Streaming.WatchStreamAsync(blockOnPendingRequest: false, ct))
        {
            switch (evt)
            {
                case PilotEvent e:
                    yield return new StreamEvent(e.Type, e.Payload);
                    break;

                case RequestInfoEvent req when req.Request.Data.As<ApprovalRequest>() is { } request:
                    active.Pending = req.Request;
                    active.PendingRequest = request;
                    active.WaitClock.Restart();
                    yield return new StreamEvent("node_started", new NodeStartedData(Nodes.Approval, 1));
                    yield return new StreamEvent("approval_required", new ApprovalRequiredData(req.Request.RequestId, request));
                    break;

                case WorkflowOutputEvent output when output.Data is RunSummary summary:
                    active.Summary = summary;
                    yield return new StreamEvent("run_completed", new RunCompletedData(summary));
                    break;

                case ExecutorFailedEvent failed:
                    active.Failed = true;
                    yield return new StreamEvent("error", new ErrorData($"{failed.ExecutorId} failed: {Describe(failed.Data)}"));
                    break;

                case WorkflowErrorEvent error:
                    active.Failed = true;
                    yield return new StreamEvent("error", new ErrorData(Describe(error.Data)));
                    break;
            }
        }
    }

    /// <summary>Keeps a run only while it is paused for approval.</summary>
    private async Task SettleAsync()
    {
        if (_active is { Pending: null } || _active is { Failed: true })
        {
            await AbandonActiveAsync();
        }
    }

    private async Task AbandonActiveAsync()
    {
        ActiveRun? active = Interlocked.Exchange(ref _active, null);
        if (active is not null) await active.Streaming.DisposeAsync();
    }

    private void Touch() => LastActivity = DateTimeOffset.UtcNow;

    private static string Describe(object? data) => data switch
    {
        Exception ex => (ex.InnerException ?? ex).Message,
        null => "Unknown error.",
        _ => data.ToString() ?? "Unknown error.",
    };

    private sealed class ActiveRun(string id, LeadRun run, StreamingRun streaming)
    {
        public string Id { get; } = id;
        public LeadRun Run { get; } = run;
        public StreamingRun Streaming { get; } = streaming;
        public ExternalRequest? Pending { get; set; }
        public ApprovalRequest? PendingRequest { get; set; }
        public RunSummary? Summary { get; set; }
        public bool Failed { get; set; }
        public Stopwatch WaitClock { get; } = new();
    }
}
