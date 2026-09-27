using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LeadPilot.Crm.Tools;

namespace LeadPilot.Core.Workflow;

public static class PilotJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}

public static class Nodes
{
    public const string Intake = "intake";
    public const string Research = "research";
    public const string History = "history";
    public const string Fit = "fit";
    public const string Compliance = "compliance";
    public const string Strategist = "strategist";
    public const string Copywriter = "copywriter";
    public const string Reviewer = "reviewer";
    public const string Approval = "approval";
    public const string CrmWriter = "crm_update";
}

public static class Decisions
{
    public const string Pursue = "pursue";
    public const string Nurture = "nurture";
    public const string Disqualify = "disqualify";
    public const string Blocked = "blocked";
}

/// <summary>Workflow input.</summary>
public sealed record LeadRequest(string LeadId);

/// <summary>Fanned out from intake to all four parallel branches.</summary>
public sealed record LeadContext(LeadSummary Lead);

public sealed record Fact(string Id, string Text, string Source);

public sealed record ResearchReport(string Summary, Fact[] Facts, string[] Signals);

/// <summary>Relationship is one of: net_new, existing_customer, former_opportunity.</summary>
public sealed record HistoryReport(string Relationship, string? AccountId, string? OwnerId, string? OwnerName, string Summary, string[] Notes);

public sealed record FitReport(int Score, string Tier, string[] Reasons);

/// <summary>Status is one of: clear, consent_required, blocked.</summary>
public sealed record PolicyReport(string Status, string[] Reasons, string[] AllowedChannels);

public sealed record LeadPlan(
    string Decision,
    string? OwnerId,
    string? OwnerName,
    string Priority,
    string Angle,
    string Rationale,
    string[] TalkingPoints,
    string[] NextSteps)
{
    /// <summary>Rules the code enforced over the model's plan, shown to the rep.</summary>
    public string[] Overrides { get; init; } = [];
}

public sealed record LeadBrief(LeadSummary Lead, ResearchReport Research, HistoryReport History, FitReport Fit, PolicyReport Policy, LeadPlan Plan);

public sealed record EmailDraft(int Version, string Subject, string Body);

public sealed record ReviewIssue(string Severity, string Message, string? Excerpt);

public sealed record ReviewResult(int Version, bool Passed, ReviewIssue[] Issues, string Summary);

/// <summary>Strategist to copywriter (first draft) and reviewer to copywriter (revision).</summary>
public sealed record DraftRequest(LeadBrief Brief, EmailDraft? Previous, ReviewResult? Feedback);

public sealed record DraftForReview(LeadBrief Brief, EmailDraft Draft);

public sealed record PlannedWrite(string Tool, Dictionary<string, object?> Args, string Description);

/// <summary>Sent to the rep through the workflow's request port.</summary>
public sealed record ApprovalRequest(
    string LeadId,
    string LeadName,
    string Company,
    LeadPlan Plan,
    FitReport Fit,
    PolicyReport Policy,
    EmailDraft? Draft,
    ReviewResult? Review,
    string[] ProposedChanges);

public sealed record ApprovalDecision(bool Approved, string? Subject = null, string? Body = null, string? Note = null);

public sealed record RunSummary(string LeadId, string Decision, bool Approved, string Outcome, string[] Changes, string[] Errors);
