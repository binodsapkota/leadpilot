using System.Globalization;
using LeadPilot.Core.Workflow;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Policy;
using LeadPilot.Crm.Tools;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Demo.Brains;

internal sealed class ResearcherBrain : DemoBrain
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public override int PlanMs => 700;

    public override int ComposeMs => 1300;

    public override ChatMessage Respond(DemoTurn turn)
    {
        LeadSummary lead = turn.Ctx<LeadSummary>("lead");
        if (!turn.Has("enrich_company"))
        {
            return Call(("enrich_company", new { domain = lead.Domain }), ("search_news", new { domain = lead.Domain }));
        }

        CompanyLookup lookup = turn.Result<CompanyLookup>("enrich_company");
        NewsItem[] news = turn.Has("search_news") ? turn.Result<NewsItem[]>("search_news") : [];
        var facts = new List<Fact>();
        var signals = new List<string>();

        if (lead.Source.Contains("demo request", StringComparison.OrdinalIgnoreCase)) signals.Add("Inbound demo request");
        if (lead.Message.Length > 0) facts.Add(new Fact("", $"Lead wrote: \"{lead.Message}\"", "lead form"));

        if (lookup.Company is not { } company)
        {
            return Json(new ResearchReport(
                $"No company profile exists for {lead.Domain}, which looks like a personal email domain. Nothing verifiable to research.",
                Number(facts), [.. signals]));
        }

        facts.Add(new Fact("", $"{company.Name} is a {company.Industry} company in {company.Headquarters} with about {company.Employees.ToString("N0", Us)} employees.", "enrich_company"));
        facts.Add(new Fact("", company.Description, "enrich_company"));
        if (company.TechStack.Length > 0) facts.Add(new Fact("", $"Known tech stack: {string.Join(", ", company.TechStack)}.", "enrich_company"));

        foreach (NewsItem item in news)
        {
            facts.Add(new Fact("", $"{item.Headline} ({item.Date.ToString("MMM d, yyyy", Us)}). {item.Summary}", item.Id));
            string h = item.Headline.ToLowerInvariant();
            if (h.Contains("raises") || h.Contains("series")) signals.Add("Fresh funding");
            else if (h.Contains("hiring")) signals.Add("Hiring finance operations roles");
            else if (h.Contains("cfo")) signals.Add("New finance leader");
            else if (h.Contains("move to") || h.Contains("migrat")) signals.Add("Completed ERP migration");
            else if (h.Contains("launches")) signals.Add("New billing model launched");
            else if (h.Contains("acquires") || h.Contains("opens")) signals.Add("Expanding footprint");
        }

        string headline = news.Length > 0 ? $" Most recent news: {news[0].Headline.TrimEnd('.')}." : "";
        return Json(new ResearchReport(
            $"{company.Name} is a {company.Employees.ToString("N0", Us)}-person {company.Industry} company based in {company.Headquarters}.{headline}",
            Number(facts), [.. signals.Distinct()]));
    }

    private static Fact[] Number(List<Fact> facts) => [.. facts.Select((f, i) => f with { Id = $"F{i + 1}" })];
}

internal sealed class HistorianBrain : DemoBrain
{
    public override int PlanMs => 450;

    public override int ComposeMs => 550;

    public override ChatMessage Respond(DemoTurn turn)
    {
        LeadSummary lead = turn.Ctx<LeadSummary>("lead");
        if (!turn.Has("find_account")) return Call(("find_account", new { domain = lead.Domain }));

        AccountMatch match = turn.Result<AccountMatch>("find_account");
        if (!match.Found || match.Account is null)
        {
            return Json(new HistoryReport("net_new", null, null, null, $"No account for {lead.Domain} in the CRM. This is a net-new logo.", []));
        }

        if (!turn.Has("get_account_history")) return Call(("get_account_history", new { accountId = match.Account.Id }));

        AccountHistory history = turn.Result<AccountHistory>("get_account_history");
        Account account = history.Account;
        var notes = new List<string>();

        if (account.Type == "Customer")
        {
            notes.Add($"Owns {string.Join(", ", account.Products)}; ARR ${account.Arr:N0}; renewal {account.RenewalDate:yyyy-MM-dd}.");
            if (account.CsmName is not null) notes.Add($"CSM: {account.CsmName}.");
            notes.AddRange(history.Activities.Take(2).Select(a => $"{a.On:yyyy-MM-dd} {a.Type.ToLowerInvariant()} ({a.By}): {a.Summary}"));
            return Json(new HistoryReport("existing_customer", account.Id, account.OwnerId, history.OwnerName,
                $"{account.Name} is an existing customer since {account.CustomerSince:yyyy} ({string.Join(", ", account.Products)}), owned by {history.OwnerName}. " +
                $"Treat this as an expansion conversation, coordinated with {account.CsmName ?? "the account team"}.",
                [.. notes]));
        }

        Opportunity? lost = history.Opportunities.Where(o => o.Stage == "Closed Lost").OrderByDescending(o => o.CloseDate).FirstOrDefault();
        if (lost is not null) notes.Add($"{lost.Name} closed lost on {lost.CloseDate:yyyy-MM-dd}: {lost.LossReason}");
        notes.AddRange(history.Activities.Take(2).Select(a => $"{a.On:yyyy-MM-dd} {a.Type.ToLowerInvariant()} ({a.By}): {a.Summary}"));
        return Json(new HistoryReport("former_opportunity", account.Id, account.OwnerId, history.OwnerName,
            lost is null
                ? $"{account.Name} was worked before by {history.OwnerName} but never closed."
                : $"{account.Name} was a {lost.CloseDate:yyyy} opportunity that closed lost. Reason: {lost.LossReason} Owner: {history.OwnerName}.",
            [.. notes]));
    }
}

internal sealed class FitBrain : DemoBrain
{
    public override int PlanMs => 300;

    public override int ComposeMs => 350;

    public override ChatMessage Respond(DemoTurn turn)
    {
        LeadSummary lead = turn.Ctx<LeadSummary>("lead");
        if (!turn.Has("score_lead")) return Call(("get_icp_rubric", null), ("score_lead", new { leadId = lead.Id }));

        FitScore score = turn.Result<FitScore>("score_lead");
        string[] reasons =
        [
            .. score.Breakdown.OrderByDescending(l => l.Points).Select(l => $"{l.Criterion}: {l.Detail} ({l.Points}/{l.Max})"),
        ];
        return Json(new FitReport(score.Score, score.Tier, reasons));
    }
}
