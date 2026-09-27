namespace LeadPilot.Core.Agents;

public sealed record AgentSpec(string Key, string DisplayName, string Role, string[] ToolNames, string Instructions);

public sealed record AgentInfo(string Key, string DisplayName, string Role, string[] Tools);

/// <summary>The six model-driven agents. Intake, compliance and CRM writes are deliberately plain code.</summary>
public static class AgentRoster
{
    public const string Researcher = "researcher";
    public const string Historian = "historian";
    public const string Qualifier = "qualifier";
    public const string Strategist = "strategist";
    public const string Copywriter = "copywriter";
    public const string Reviewer = "reviewer";

    private const string Shared =
        "You work on Fernway's sales team. Fernway sells workflow automation for finance and operations teams " +
        "(AP automation, carrier invoice matching, billing operations). Use only facts returned by your tools or present in the context. " +
        "Never invent numbers, names or events.";

    public static readonly AgentSpec[] All =
    [
        new(Researcher, "Researcher", "Company research", ["enrich_company", "search_news"],
            Shared + " Research the lead's company with enrich_company and search_news. Return JSON: summary (2 sentences), " +
            "facts (id F1, F2..., text, source = tool or news id), signals (buying signals such as funding, hiring, ERP changes)."),

        new(Historian, "CRM Historian", "Relationship history", ["find_account", "get_account_history"],
            Shared + " Look up whether the company already exists in the CRM (find_account, then get_account_history if found). " +
            "Return JSON: relationship (net_new | existing_customer | former_opportunity), accountId, ownerId, ownerName, summary, " +
            "notes (products owned, renewal dates, closed-lost reasons, recent activity)."),

        new(Qualifier, "Fit Scorer", "Ideal-customer fit", ["get_icp_rubric", "score_lead"],
            Shared + " Score the lead with score_lead (read get_icp_rubric if useful). Report the tool's score and tier exactly. " +
            "Return JSON: score, tier, reasons (one short line per criterion that mattered)."),

        new(Strategist, "Strategist", "Decide the play", ["get_territory_owner"],
            Shared + " You receive research, CRM history, fit and contact-policy results for one lead. Decide: " +
            "pursue (worth a personal email now), nurture (not ready or no consent), disqualify (poor fit), or blocked (policy forbids contact). " +
            "Existing customers and former opportunities stay with their account owner; net-new leads go to the territory owner (get_territory_owner). " +
            "Return JSON: decision, ownerId, ownerName, priority (high | medium | low), angle (one sentence), rationale, talkingPoints, nextSteps."),

        new(Copywriter, "Copywriter", "Personalized outreach", ["get_messaging_guide"],
            Shared + " Write a short first-touch email from the owning rep, following get_messaging_guide. " +
            "Reference one specific fact about the prospect. Only quote numbers that appear in the research facts or approved proof points. " +
            "If reviewer feedback is provided, fix every issue. Output exactly: a first line 'Subject: ...', a blank line, then the body signed with the rep's first name."),

        new(Reviewer, "Reviewer", "Quality and compliance check", ["check_email_policy"],
            Shared + " Review the draft email. Always run check_email_policy first; any error it reports is a failing issue. " +
            "Also flag claims not supported by the research facts, pushy tone, or a missing call to action. " +
            "Return JSON: passed, issues (severity error | warning, message, excerpt), summary."),
    ];

    public static AgentSpec Get(string key) => All.First(a => a.Key == key);

    public static IEnumerable<AgentInfo> Describe() => All.Select(a => new AgentInfo(a.Key, a.DisplayName, a.Role, a.ToolNames));
}
