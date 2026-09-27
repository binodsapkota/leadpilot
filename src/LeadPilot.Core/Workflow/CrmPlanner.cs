namespace LeadPilot.Core.Workflow;

/// <summary>
/// Turns a decision into the exact CRM writes that will run after approval. The rep sees this list before
/// anything is written, and the writer executes exactly this list.
/// </summary>
public static class CrmPlanner
{
    public const string NurtureQueue = "R-NQ";

    public static ApprovalRequest Plan(LeadRun run, LeadBrief brief, EmailDraft? draft, ReviewResult? review)
    {
        var lead = brief.Lead;
        var plan = brief.Plan;
        var writes = new List<PlannedWrite>();

        switch (plan.Decision)
        {
            case Decisions.Pursue:
                writes.Add(Update(lead.Id, "Working", plan.OwnerId, brief, plan.Angle,
                    $"Set {lead.Name} to Working, owned by {plan.OwnerName}, fit {brief.Fit.Score} ({brief.Fit.Tier})"));
                writes.Add(Log(lead.Id, $"Qualified by LeadPilot (fit {brief.Fit.Score}, tier {brief.Fit.Tier})", plan.Rationale));
                string task = Clip(plan.NextSteps.FirstOrDefault() ?? $"Follow up with {lead.Name.Split(' ')[0]} at {lead.Company}", 90);
                writes.Add(new("create_task", new() { ["leadId"] = lead.Id, ["ownerId"] = plan.OwnerId, ["title"] = task, ["dueInDays"] = 2 },
                    $"Task for {plan.OwnerName}: {task}"));
                if (draft is not null)
                {
                    writes.Add(new("queue_email", new() { ["leadId"] = lead.Id, ["fromRepId"] = plan.OwnerId, ["subject"] = draft.Subject, ["body"] = draft.Body },
                        $"Queue email from {plan.OwnerName} to {lead.Email}: \"{draft.Subject}\""));
                }
                break;

            case Decisions.Nurture:
                writes.Add(Update(lead.Id, "Nurture", NurtureQueue, brief, plan.Rationale, $"Set {lead.Name} to Nurture (marketing nurture queue)"));
                writes.Add(Log(lead.Id, "Routed to nurture by LeadPilot", Join(plan.Rationale, brief.Policy.Reasons)));
                break;

            case Decisions.Disqualify:
                writes.Add(Update(lead.Id, "Disqualified", null, brief, plan.Rationale, $"Set {lead.Name} to Disqualified"));
                writes.Add(Log(lead.Id, "Disqualified by LeadPilot", plan.Rationale));
                break;

            default:
                writes.Add(Update(lead.Id, "Do not contact", null, brief, string.Join(" ", brief.Policy.Reasons), $"Set {lead.Name} to Do not contact"));
                writes.Add(Log(lead.Id, "Outreach blocked by contact policy", Join(plan.Rationale, brief.Policy.Reasons)));
                break;
        }

        run.PlannedWrites = writes;
        run.Approval = new ApprovalRequest(
            lead.Id, lead.Name, lead.Company, plan, brief.Fit, brief.Policy, draft, review, [.. writes.Select(w => w.Description)]);
        return run.Approval;
    }

    public static List<PlannedWrite> ForRejection(string leadId, string leadName, string? note) =>
    [
        new("update_lead", new() { ["leadId"] = leadId, ["status"] = "Needs review", ["note"] = note ?? "Rep rejected the recommendation." },
            $"Set {leadName} to Needs review"),
        Log(leadId, "Rep rejected LeadPilot recommendation", note ?? "No reason given."),
    ];

    private static PlannedWrite Update(string leadId, string status, string? ownerId, LeadBrief brief, string note, string description) =>
        new("update_lead", new()
        {
            ["leadId"] = leadId,
            ["status"] = status,
            ["ownerId"] = ownerId,
            ["score"] = brief.Fit.Score,
            ["tier"] = brief.Fit.Tier,
            ["accountId"] = brief.History.AccountId,
            ["note"] = Clip(note, 280),
        }, description);

    private static PlannedWrite Log(string leadId, string subject, string summary) =>
        new("log_activity", new() { ["leadId"] = leadId, ["type"] = "Note", ["subject"] = subject, ["summary"] = Clip(summary, 600) },
            $"Log note: {subject}");

    private static string Join(string first, string[] rest) => string.Join(" ", new[] { first }.Concat(rest).Where(s => s.Length > 0));

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";
}
