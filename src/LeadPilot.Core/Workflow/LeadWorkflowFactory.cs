using LeadPilot.Core.Workflow.Executors;
using Microsoft.Agents.AI.Workflows;

namespace LeadPilot.Core.Workflow;

/// <summary>
/// The lead qualification graph:
/// intake fans out to research, history, fit and compliance in parallel; a barrier joins them at the strategist;
/// pursued leads loop through copywriter and reviewer; everything stops at the approval port before CRM writes.
/// </summary>
public static class LeadWorkflowFactory
{
    public static Microsoft.Agents.AI.Workflows.Workflow Build(LeadRun run)
    {
        var intake = new IntakeExecutor(run);
        var research = new ResearchExecutor(run);
        var history = new HistoryExecutor(run);
        var fit = new FitExecutor(run);
        var compliance = new ComplianceExecutor(run);
        var strategist = new StrategistExecutor(run);
        var copywriter = new CopywriterExecutor(run);
        var reviewer = new ReviewerExecutor(run);
        var approval = RequestPort.Create<ApprovalRequest, ApprovalDecision>(Nodes.Approval);
        var crmWriter = new CrmWriterExecutor(run);

        return new WorkflowBuilder(intake)
            .WithName("lead-qualification")
            .AddFanOutEdge(intake, [research, history, fit, compliance])
            .AddFanInBarrierEdge([research, history, fit, compliance], strategist)
            .AddEdge<DraftRequest>(strategist, copywriter, m => m is not null)
            .AddEdge<ApprovalRequest>(strategist, approval, m => m is not null)
            .AddEdge(copywriter, reviewer)
            .AddEdge<DraftRequest>(reviewer, copywriter, m => m is not null)
            .AddEdge<ApprovalRequest>(reviewer, approval, m => m is not null)
            .AddEdge(approval, crmWriter)
            .WithOutputFrom(crmWriter)
            .Build();
    }

    /// <summary>Static description of the graph for the UI.</summary>
    public static object Describe() => new
    {
        nodes = new object[]
        {
            new { id = Nodes.Intake, label = "Intake", kind = "code", detail = "get_lead" },
            new { id = Nodes.Research, label = "Researcher", kind = "agent", detail = "enrich_company, search_news" },
            new { id = Nodes.History, label = "CRM Historian", kind = "agent", detail = "find_account, get_account_history" },
            new { id = Nodes.Fit, label = "Fit Scorer", kind = "agent", detail = "get_icp_rubric, score_lead" },
            new { id = Nodes.Compliance, label = "Compliance", kind = "code", detail = "check_contact_policy" },
            new { id = Nodes.Strategist, label = "Strategist", kind = "agent", detail = "get_territory_owner" },
            new { id = Nodes.Copywriter, label = "Copywriter", kind = "agent", detail = "get_messaging_guide" },
            new { id = Nodes.Reviewer, label = "Reviewer", kind = "agent", detail = "check_email_policy" },
            new { id = Nodes.Approval, label = "Rep approval", kind = "human", detail = "request port" },
            new { id = Nodes.CrmWriter, label = "CRM update", kind = "code", detail = "update_lead, log_activity, create_task, queue_email" },
        },
        edges = new object[]
        {
            new { from = Nodes.Intake, to = Nodes.Research, kind = "fan-out" },
            new { from = Nodes.Intake, to = Nodes.History, kind = "fan-out" },
            new { from = Nodes.Intake, to = Nodes.Fit, kind = "fan-out" },
            new { from = Nodes.Intake, to = Nodes.Compliance, kind = "fan-out" },
            new { from = Nodes.Research, to = Nodes.Strategist, kind = "fan-in" },
            new { from = Nodes.History, to = Nodes.Strategist, kind = "fan-in" },
            new { from = Nodes.Fit, to = Nodes.Strategist, kind = "fan-in" },
            new { from = Nodes.Compliance, to = Nodes.Strategist, kind = "fan-in" },
            new { from = Nodes.Strategist, to = Nodes.Copywriter, kind = "pursue" },
            new { from = Nodes.Strategist, to = Nodes.Approval, kind = "no email" },
            new { from = Nodes.Copywriter, to = Nodes.Reviewer, kind = "draft" },
            new { from = Nodes.Reviewer, to = Nodes.Copywriter, kind = "revise" },
            new { from = Nodes.Reviewer, to = Nodes.Approval, kind = "approved" },
            new { from = Nodes.Approval, to = Nodes.CrmWriter, kind = "decision" },
        },
    };
}
