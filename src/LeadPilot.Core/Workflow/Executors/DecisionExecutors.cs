using System.Diagnostics;
using LeadPilot.Core.Agents;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Policy;
using Microsoft.Agents.AI.Workflows;

namespace LeadPilot.Core.Workflow.Executors;

/// <summary>
/// Fan-in point. The barrier edge delivers all four branch reports in one superstep; once they are all here the
/// strategist agent decides the play, and code enforces the rules the model is not allowed to break.
/// </summary>
[SendsMessage(typeof(DraftRequest))]
[SendsMessage(typeof(ApprovalRequest))]
public sealed partial class StrategistExecutor(LeadRun run) : Executor(Nodes.Strategist)
{
    private static readonly string[] AllowedDecisions = [Decisions.Pursue, Decisions.Nurture, Decisions.Disqualify, Decisions.Blocked];

    private ResearchReport? _research;
    private HistoryReport? _history;
    private FitReport? _fit;
    private PolicyReport? _policy;

    [MessageHandler]
    private ValueTask OnResearchAsync(ResearchReport report, IWorkflowContext ctx, CancellationToken ct = default)
    {
        _research = report;
        return TryDecideAsync(ctx, ct);
    }

    [MessageHandler]
    private ValueTask OnHistoryAsync(HistoryReport report, IWorkflowContext ctx, CancellationToken ct = default)
    {
        _history = report;
        return TryDecideAsync(ctx, ct);
    }

    [MessageHandler]
    private ValueTask OnFitAsync(FitReport report, IWorkflowContext ctx, CancellationToken ct = default)
    {
        _fit = report;
        return TryDecideAsync(ctx, ct);
    }

    [MessageHandler]
    private ValueTask OnPolicyAsync(PolicyReport report, IWorkflowContext ctx, CancellationToken ct = default)
    {
        _policy = report;
        return TryDecideAsync(ctx, ct);
    }

    private async ValueTask TryDecideAsync(IWorkflowContext ctx, CancellationToken ct)
    {
        if (_research is null || _history is null || _fit is null || _policy is null || run.Lead is null) return;

        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.Strategist, ct: ct);

        var capture = new ToolCapture();
        LeadPlan proposed = await Steps.RunAgentJsonAsync<LeadPlan>(
            run.Agent(AgentRoster.Strategist),
            Nodes.Strategist,
            Steps.Prompt("Decide how to handle this lead.", new { lead = run.Lead, research = _research, history = _history, fit = _fit, policy = _policy }),
            ctx, capture, ct);

        LeadPlan plan = await GovernAsync(proposed, capture, ctx, ct);
        var brief = new LeadBrief(run.Lead, _research, _history, _fit, _policy, plan);
        run.Brief = brief;

        await Steps.ReportAsync(ctx, Nodes.Strategist, "plan", plan, ct);
        await Steps.CompleteAsync(ctx, Nodes.Strategist, sw, plan.Decision + (plan.OwnerName is null ? "" : $", owner {plan.OwnerName}"), ct);

        if (plan.Decision == Decisions.Pursue)
        {
            await ctx.SendMessageAsync(new DraftRequest(brief, null, null), cancellationToken: ct);
        }
        else
        {
            await ctx.SendMessageAsync(CrmPlanner.Plan(run, brief, null, null), cancellationToken: ct);
        }
    }

    private async Task<LeadPlan> GovernAsync(LeadPlan plan, ToolCapture capture, IWorkflowContext ctx, CancellationToken ct)
    {
        var overrides = new List<string>();
        string decision = AllowedDecisions.Contains(plan.Decision) ? plan.Decision : Decisions.Nurture;

        if (_policy!.Status == "blocked" && decision != Decisions.Blocked)
        {
            overrides.Add($"Contact policy is blocked, so \"{decision}\" became \"blocked\".");
            decision = Decisions.Blocked;
        }
        else if (_policy.Status == "consent_required" && decision == Decisions.Pursue)
        {
            overrides.Add("No GDPR marketing consent, so \"pursue\" became \"nurture\".");
            decision = Decisions.Nurture;
        }

        if (decision == Decisions.Pursue && _fit!.Tier == "D" && _history!.Relationship == "net_new")
        {
            overrides.Add("Tier D net-new leads are never pursued, so \"pursue\" became \"disqualify\".");
            decision = Decisions.Disqualify;
        }

        string? ownerId = plan.OwnerId;
        string? ownerName = plan.OwnerName;
        if (decision == Decisions.Pursue)
        {
            if (_history!.Relationship != "net_new" && _history.OwnerId is not null)
            {
                if (ownerId != _history.OwnerId) overrides.Add($"Existing relationship stays with the account owner, {_history.OwnerName}.");
                (ownerId, ownerName) = (_history.OwnerId, _history.OwnerName);
            }
            else
            {
                if (!capture.TryGet("get_territory_owner", out Rep territory))
                {
                    int employees = await EmployeesAsync(ctx, ct);
                    territory = await Steps.CallCrmAsync<Rep>(run, ctx, Nodes.Strategist, "get_territory_owner",
                        new() { ["country"] = run.Lead!.Country, ["employees"] = employees }, ct);
                }
                if (ownerId != territory.Id) overrides.Add($"Net-new leads go to the territory owner, {territory.Name}.");
                (ownerId, ownerName) = (territory.Id, territory.Name);
            }
        }
        else
        {
            (ownerId, ownerName) = decision == Decisions.Nurture ? (CrmPlanner.NurtureQueue, "Nurture queue") : ((string?)null, (string?)null);
        }

        bool changed = decision != plan.Decision;
        return plan with
        {
            Decision = decision,
            OwnerId = ownerId,
            OwnerName = ownerName,
            Priority = changed ? "low" : plan.Priority ?? "medium",
            Angle = changed ? "" : plan.Angle ?? "",
            Rationale = changed ? $"Strategist proposed \"{plan.Decision}\": {plan.Rationale}" : plan.Rationale ?? "",
            TalkingPoints = changed ? [] : plan.TalkingPoints ?? [],
            NextSteps = changed ? DefaultNextSteps(decision) : plan.NextSteps ?? [],
            Overrides = [.. overrides],
        };
    }

    private string[] DefaultNextSteps(string decision) => decision switch
    {
        Decisions.Nurture when _policy!.Status == "consent_required" => ["Send a double opt-in consent request before any sales email."],
        Decisions.Nurture => ["Add to the nurture track.", "Re-score if they engage again."],
        Decisions.Disqualify => ["No sales follow-up."],
        _ => ["Do not email, call or add to sequences."],
    };

    private async Task<int> EmployeesAsync(IWorkflowContext ctx, CancellationToken ct)
    {
        var lookup = await Steps.CallCrmAsync<Crm.Tools.CompanyLookup>(run, ctx, Nodes.Strategist, "enrich_company", new() { ["domain"] = run.Lead!.Domain }, ct);
        return lookup.Company?.Employees ?? 0;
    }
}

[SendsMessage(typeof(DraftForReview))]
public sealed partial class CopywriterExecutor(LeadRun run) : Executor(Nodes.Copywriter)
{
    [MessageHandler]
    private async ValueTask HandleAsync(DraftRequest request, IWorkflowContext ctx, CancellationToken ct = default)
    {
        int version = run.Drafts.Count + 1;
        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.Copywriter, version, ct);

        LeadBrief brief = request.Brief;
        string task = request.Feedback is null
            ? "Write the first-touch email."
            : "Revise the email. Fix every reviewer issue and keep what worked.";
        string text = await Steps.RunAgentAsync(
            run.Agent(AgentRoster.Copywriter),
            Nodes.Copywriter,
            Steps.Prompt(task, new
            {
                version,
                lead = brief.Lead,
                owner = new { id = brief.Plan.OwnerId, name = brief.Plan.OwnerName },
                relationship = brief.History.Relationship,
                history = brief.History.Summary,
                historyNotes = brief.History.Notes,
                angle = brief.Plan.Angle,
                talkingPoints = brief.Plan.TalkingPoints,
                facts = brief.Research.Facts,
                previous = request.Previous,
                reviewerIssues = request.Feedback?.Issues,
            }),
            ctx, tokenVersion: version, ct: ct);

        EmailDraft draft = Parse(version, text);
        run.Drafts.Add(draft);

        await Steps.ReportAsync(ctx, Nodes.Copywriter, "draft", draft, ct);
        await Steps.CompleteAsync(ctx, Nodes.Copywriter, sw, $"Draft v{version}: \"{draft.Subject}\"", ct);
        await ctx.SendMessageAsync(new DraftForReview(brief, draft), cancellationToken: ct);
    }

    internal static EmailDraft Parse(int version, string text)
    {
        string[] lines = text.Replace("\r", "").Trim().Split('\n');
        int subjectLine = Array.FindIndex(lines, l => l.TrimStart().StartsWith("Subject:", StringComparison.OrdinalIgnoreCase));
        if (subjectLine < 0) return new EmailDraft(version, "(no subject)", text.Trim());

        string subject = lines[subjectLine].Trim()["Subject:".Length..].Trim();
        string body = string.Join('\n', lines.Skip(subjectLine + 1)).Trim();
        return new EmailDraft(version, subject, body);
    }
}

/// <summary>
/// Critic in the revision loop. Failing drafts go back to the copywriter; after <see cref="LeadRun.MaxDrafts"/>
/// attempts the best effort goes to the rep with the open issues attached.
/// </summary>
[SendsMessage(typeof(DraftRequest))]
[SendsMessage(typeof(ApprovalRequest))]
public sealed partial class ReviewerExecutor(LeadRun run) : Executor(Nodes.Reviewer)
{
    internal sealed record ReviewerOutput(bool Passed, ReviewIssue[]? Issues, string? Summary);

    [MessageHandler]
    private async ValueTask HandleAsync(DraftForReview input, IWorkflowContext ctx, CancellationToken ct = default)
    {
        EmailDraft draft = input.Draft;
        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.Reviewer, draft.Version, ct);

        var capture = new ToolCapture();
        ReviewerOutput output = await Steps.RunAgentJsonAsync<ReviewerOutput>(
            run.Agent(AgentRoster.Reviewer),
            Nodes.Reviewer,
            Steps.Prompt("Review this draft email.", new { lead = input.Brief.Lead, facts = input.Brief.Research.Facts, draft }),
            ctx, capture, ct);

        // The policy check is authoritative: a model cannot wave through a draft the rules reject.
        if (!capture.TryGet("check_email_policy", out EmailPolicyResult policy))
        {
            policy = await Steps.CallCrmAsync<EmailPolicyResult>(run, ctx, Nodes.Reviewer, "check_email_policy",
                new() { ["leadId"] = input.Brief.Lead.Id, ["subject"] = draft.Subject, ["body"] = draft.Body }, ct);
        }

        var issues = (output.Issues ?? []).ToList();
        foreach (EmailIssue issue in policy.Issues.Where(i => !issues.Any(x => x.Message == i.Message)))
        {
            issues.Add(new ReviewIssue(issue.Severity, issue.Message, issue.Excerpt));
        }
        bool passed = output.Passed && policy.Passed;
        var review = new ReviewResult(draft.Version, passed, [.. issues], output.Summary ?? (passed ? "Looks good." : "Needs changes."));
        run.Reviews.Add(review);

        await Steps.ReportAsync(ctx, Nodes.Reviewer, "review", review, ct);
        int errors = issues.Count(i => i.Severity == "error");
        await Steps.CompleteAsync(ctx, Nodes.Reviewer, sw, passed ? $"v{draft.Version} approved" : $"v{draft.Version}: {errors} issue(s), sent back", ct);

        if (passed || run.Drafts.Count >= run.MaxDrafts)
        {
            await ctx.SendMessageAsync(CrmPlanner.Plan(run, input.Brief, draft, review), cancellationToken: ct);
        }
        else
        {
            await ctx.SendMessageAsync(new DraftRequest(input.Brief, draft, review), cancellationToken: ct);
        }
    }
}

/// <summary>Writes to the CRM only after the rep approves, executing exactly the planned list.</summary>
[YieldsOutput(typeof(RunSummary))]
public sealed partial class CrmWriterExecutor(LeadRun run) : Executor(Nodes.CrmWriter)
{
    [MessageHandler]
    private async ValueTask HandleAsync(ApprovalDecision decision, IWorkflowContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await Steps.StartAsync(ctx, Nodes.CrmWriter, ct: ct);

        ApprovalRequest request = run.Approval ?? throw new InvalidOperationException("No approval request recorded for this run.");
        List<PlannedWrite> writes = decision.Approved
            ? [.. run.PlannedWrites.Select(w => w.Tool == "queue_email" ? WithEdits(w, decision) : w)]
            : CrmPlanner.ForRejection(request.LeadId, request.LeadName, decision.Note);

        var done = new List<string>();
        var errors = new List<string>();
        foreach (PlannedWrite write in writes)
        {
            try
            {
                var result = await Steps.CallCrmAsync<Crm.Tools.WriteResult>(run, ctx, Nodes.CrmWriter, write.Tool, write.Args, ct);
                done.Add(write.Description);
                await ctx.AddEventAsync(new PilotEvent("crm_write", new CrmWriteData(write.Tool, write.Description, true, result.Message)), ct);
            }
            catch (Mcp.CrmToolException ex)
            {
                errors.Add($"{write.Tool}: {ex.Message}");
                await ctx.AddEventAsync(new PilotEvent("crm_write", new CrmWriteData(write.Tool, write.Description, false, ex.Message)), ct);
            }
        }

        string outcome = !decision.Approved
            ? "Rep rejected the recommendation; lead flagged for review."
            : errors.Count > 0 ? $"Approved, but {errors.Count} write(s) were refused by the CRM." : Outcome(request.Plan.Decision);

        await Steps.CompleteAsync(ctx, Nodes.CrmWriter, sw, $"{done.Count} change(s) written" + (errors.Count > 0 ? $", {errors.Count} refused" : ""), ct);
        await ctx.YieldOutputAsync(new RunSummary(request.LeadId, request.Plan.Decision, decision.Approved, outcome, [.. done], [.. errors]), ct);
    }

    private static PlannedWrite WithEdits(PlannedWrite write, ApprovalDecision decision)
    {
        var args = new Dictionary<string, object?>(write.Args);
        if (!string.IsNullOrWhiteSpace(decision.Subject)) args["subject"] = decision.Subject.Trim();
        if (!string.IsNullOrWhiteSpace(decision.Body)) args["body"] = decision.Body.Trim();
        return write with { Args = args };
    }

    private static string Outcome(string decision) => decision switch
    {
        Decisions.Pursue => "Lead is being worked; follow-up task created and email queued.",
        Decisions.Nurture => "Lead routed to marketing nurture.",
        Decisions.Disqualify => "Lead disqualified.",
        _ => "Lead marked do-not-contact.",
    };
}
