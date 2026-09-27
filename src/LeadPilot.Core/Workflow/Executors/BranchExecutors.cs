using System.Diagnostics;
using LeadPilot.Core.Agents;
using LeadPilot.Crm.Policy;
using LeadPilot.Crm.Tools;
using Microsoft.Agents.AI.Workflows;

namespace LeadPilot.Core.Workflow.Executors;

/// <summary>Loads the lead from the CRM and fans it out to the four parallel branches.</summary>
[SendsMessage(typeof(LeadContext))]
public sealed partial class IntakeExecutor(LeadRun run) : Executor(Nodes.Intake)
{
    [MessageHandler]
    private async ValueTask HandleAsync(LeadRequest request, IWorkflowContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.Intake, ct: ct);

        LeadSummary lead = await Steps.CallCrmAsync<LeadSummary>(run, ctx, Nodes.Intake, "get_lead", new() { ["leadId"] = request.LeadId }, ct);
        run.Lead = lead;

        await Steps.ReportAsync(ctx, Nodes.Intake, "lead", lead, ct);
        await Steps.CompleteAsync(ctx, Nodes.Intake, sw, $"{lead.Name}, {lead.Title}{(lead.Company.Length > 0 ? $" at {lead.Company}" : "")}", ct);
        await ctx.SendMessageAsync(new LeadContext(lead), cancellationToken: ct);
    }
}

[SendsMessage(typeof(ResearchReport))]
public sealed partial class ResearchExecutor(LeadRun run) : Executor(Nodes.Research)
{
    [MessageHandler]
    private async ValueTask HandleAsync(LeadContext input, IWorkflowContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.Research, ct: ct);

        ResearchReport report = await Steps.RunAgentJsonAsync<ResearchReport>(
            run.Agent(AgentRoster.Researcher), Nodes.Research, Steps.Prompt("Research this lead's company.", new { lead = input.Lead }), ctx, ct: ct);
        report = report with { Facts = report.Facts ?? [], Signals = report.Signals ?? [], Summary = report.Summary ?? "" };

        await Steps.ReportAsync(ctx, Nodes.Research, "research", report, ct);
        await Steps.CompleteAsync(ctx, Nodes.Research, sw, $"{report.Facts.Length} facts, {report.Signals.Length} buying signals", ct);
        await ctx.SendMessageAsync(report, cancellationToken: ct);
    }
}

[SendsMessage(typeof(HistoryReport))]
public sealed partial class HistoryExecutor(LeadRun run) : Executor(Nodes.History)
{
    private static readonly string[] Relationships = ["net_new", "existing_customer", "former_opportunity"];

    [MessageHandler]
    private async ValueTask HandleAsync(LeadContext input, IWorkflowContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.History, ct: ct);

        var capture = new ToolCapture();
        HistoryReport report = await Steps.RunAgentJsonAsync<HistoryReport>(
            run.Agent(AgentRoster.Historian), Nodes.History, Steps.Prompt("Check the CRM history for this lead's company.", new { lead = input.Lead }), ctx, capture, ct);

        report = report with { Notes = report.Notes ?? [], Summary = report.Summary ?? "" };
        if (!Relationships.Contains(report.Relationship)) report = report with { Relationship = "net_new" };

        // The model may not claim an account the CRM doesn't have.
        if (capture.TryGet("find_account", out AccountMatch match) && !match.Found && report.Relationship != "net_new")
        {
            report = report with { Relationship = "net_new", AccountId = null, OwnerId = null, OwnerName = null };
        }

        await Steps.ReportAsync(ctx, Nodes.History, "history", report, ct);
        await Steps.CompleteAsync(ctx, Nodes.History, sw, report.Relationship.Replace('_', ' ') + (report.OwnerName is null ? "" : $", owner {report.OwnerName}"), ct);
        await ctx.SendMessageAsync(report, cancellationToken: ct);
    }
}

[SendsMessage(typeof(FitReport))]
public sealed partial class FitExecutor(LeadRun run) : Executor(Nodes.Fit)
{
    [MessageHandler]
    private async ValueTask HandleAsync(LeadContext input, IWorkflowContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.Fit, ct: ct);

        var capture = new ToolCapture();
        FitReport report = await Steps.RunAgentJsonAsync<FitReport>(
            run.Agent(AgentRoster.Qualifier), Nodes.Fit, Steps.Prompt("Score this lead against the ideal customer profile.", new { lead = input.Lead }), ctx, capture, ct);

        // The score always comes from the deterministic rubric, never from the model's own arithmetic.
        if (!capture.TryGet("score_lead", out FitScore score))
        {
            score = await Steps.CallCrmAsync<FitScore>(run, ctx, Nodes.Fit, "score_lead", new() { ["leadId"] = input.Lead.Id }, ct);
        }
        report = report with { Score = score.Score, Tier = score.Tier, Reasons = report.Reasons ?? [] };

        await Steps.ReportAsync(ctx, Nodes.Fit, "fit", new { report.Score, report.Tier, report.Reasons, score.Breakdown }, ct);
        await Steps.CompleteAsync(ctx, Nodes.Fit, sw, $"{report.Score}/100, tier {report.Tier}", ct);
        await ctx.SendMessageAsync(report, cancellationToken: ct);
    }
}

/// <summary>Contact policy is a rule, not a judgment call, so this branch is plain code calling the CRM's MCP tool.</summary>
[SendsMessage(typeof(PolicyReport))]
public sealed partial class ComplianceExecutor(LeadRun run) : Executor(Nodes.Compliance)
{
    [MessageHandler]
    private async ValueTask HandleAsync(LeadContext input, IWorkflowContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.Compliance, ct: ct);

        ContactPolicyResult result = await Steps.CallCrmAsync<ContactPolicyResult>(
            run, ctx, Nodes.Compliance, "check_contact_policy", new() { ["leadId"] = input.Lead.Id }, ct);
        var report = new PolicyReport(result.Status, result.Reasons, result.AllowedChannels);

        await Steps.ReportAsync(ctx, Nodes.Compliance, "policy", report, ct);
        await Steps.CompleteAsync(ctx, Nodes.Compliance, sw, report.Status.Replace('_', ' '), ct);
        await ctx.SendMessageAsync(report, cancellationToken: ct);
    }
}
