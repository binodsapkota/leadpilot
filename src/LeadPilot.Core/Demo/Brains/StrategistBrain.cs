using System.Text.RegularExpressions;
using LeadPilot.Core.Workflow;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Tools;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Demo.Brains;

/// <summary>
/// Decides the play from the four branch reports. It weighs fit and relationship but, like a real model can,
/// overlooks GDPR consent; the workflow's code guardrail catches that (visible on lead L-203).
/// </summary>
internal sealed partial class StrategistBrain : DemoBrain
{
    public override int PlanMs => 500;

    public override int ComposeMs => 900;

    public override ChatMessage Respond(DemoTurn turn)
    {
        LeadSummary lead = turn.Ctx<LeadSummary>("lead");
        ResearchReport research = turn.Ctx<ResearchReport>("research");
        HistoryReport history = turn.Ctx<HistoryReport>("history");
        FitReport fit = turn.Ctx<FitReport>("fit");
        PolicyReport policy = turn.Ctx<PolicyReport>("policy");
        string first = FirstName(lead.Name);
        string priority = fit.Tier == "A" ? "high" : fit.Tier == "B" ? "medium" : "low";

        if (policy.Status == "blocked")
        {
            return Plan(Decisions.Blocked, null, null, "low", "",
                $"Contact policy blocks any outreach: {string.Join(" ", policy.Reasons)} Fit ({fit.Score}, tier {fit.Tier}) is irrelevant until that changes.",
                [], ["Do not email, call or add to sequences.", "Keep the record for reporting only."]);
        }

        if (history.Relationship == "existing_customer")
        {
            string csm = history.Notes.Select(n => CsmPattern().Match(n)).FirstOrDefault(m => m.Success)?.Groups[1].Value ?? "the CSM";
            return Plan(Decisions.Pursue, history.OwnerId, history.OwnerName, "high",
                $"Expansion, not a cold pitch: {lead.Company} already runs Fernway for AP and is asking about billing operations.",
                $"Existing customer (fit {fit.Score}, tier {fit.Tier}) with a live expansion question. {history.OwnerName} owns the account, so the reply comes from them, coordinated with {csm}.",
                [.. research.Signals.Take(2), .. history.Notes.Take(2)],
                [$"Brief {csm} before replying to {first}", $"Offer {first} a billing-operations working session"]);
        }

        if (fit.Tier == "D")
        {
            return Plan(Decisions.Disqualify, null, null, "low", "",
                $"Fit {fit.Score}/100 (tier D). {research.Summary} No buying authority or company to sell to.",
                [], ["No sales follow-up."]);
        }

        if (fit.Tier == "C")
        {
            return Plan(Decisions.Nurture, null, null, "low", "",
                $"Fit {fit.Score}/100 (tier C): real company, but not enough signal to spend rep time yet.",
                [], ["Add to the nurture track.", "Re-score if they engage again."]);
        }

        if (history.Relationship == "former_opportunity")
        {
            return Plan(Decisions.Pursue, history.OwnerId, history.OwnerName, priority,
                $"Re-engage: {lead.Company} came back on their own, and the reason we lost last time may no longer apply.",
                $"Former opportunity (fit {fit.Score}, tier {fit.Tier}). {history.Summary} New signals: {Join(research.Signals)}.",
                [.. research.Signals.Take(2), .. history.Notes.Take(1)],
                [$"Call {first} to walk through what changed since the last evaluation", "Prepare a connector demo for the discovery call"]);
        }

        if (!turn.Has("get_territory_owner"))
        {
            return Call(("get_territory_owner", new { country = lead.Country, employees = Employees(research) }));
        }

        Rep owner = turn.Result<Rep>("get_territory_owner");
        string angle = lead.Message.Contains("invoice", StringComparison.OrdinalIgnoreCase)
            ? $"Timing: {Join(research.Signals).ToLowerInvariant()}. Lead the conversation with carrier invoice automation."
            : $"Timing: {Join(research.Signals).ToLowerInvariant()}. Open with a short discovery call.";
        return Plan(Decisions.Pursue, owner.Id, owner.Name, priority, angle,
            $"Net-new logo, fit {fit.Score}/100 (tier {fit.Tier}) with {research.Signals.Length} buying signal(s). Routed to {owner.Name} ({owner.Territory}).",
            [.. research.Signals.Take(3)],
            [$"Call {first} within 2 business days to book discovery", "Send the approved first-touch email"]);
    }

    private static ChatMessage Plan(string decision, string? ownerId, string? ownerName, string priority, string angle, string rationale, string[] talkingPoints, string[] nextSteps) =>
        Json(new LeadPlan(decision, ownerId, ownerName, priority, angle, rationale, talkingPoints, nextSteps));

    private static string Join(string[] items) => items.Length == 0 ? "none" : string.Join(", ", items);

    private static int Employees(ResearchReport research)
    {
        foreach (Fact fact in research.Facts)
        {
            Match m = EmployeesPattern().Match(fact.Text);
            if (m.Success) return int.Parse(m.Groups[1].Value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
        }
        return 0;
    }

    [GeneratedRegex(@"about ([\d,]+) employees")]
    private static partial Regex EmployeesPattern();

    [GeneratedRegex(@"CSM: ([^.]+)\.")]
    private static partial Regex CsmPattern();
}
