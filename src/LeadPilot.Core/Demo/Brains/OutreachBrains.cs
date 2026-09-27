using System.Text;
using System.Text.RegularExpressions;
using LeadPilot.Core.Workflow;
using LeadPilot.Crm.Policy;
using LeadPilot.Crm.Tools;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Demo.Brains;

/// <summary>
/// Writes the first-touch email. For net-new logistics leads its first draft includes an invented benchmark,
/// the kind of confident statistic real models produce, so the reviewer loop has something real to catch.
/// </summary>
internal sealed partial class CopywriterBrain : DemoBrain
{
    private sealed record Owner(string? Id, string? Name);

    public override int PlanMs => 350;

    public override int ComposeMs => 500;

    public override int TypingMs => 30;

    public override ChatMessage Respond(DemoTurn turn)
    {
        if (!turn.Has("get_messaging_guide")) return Call(("get_messaging_guide", null));

        MessagingGuide guide = turn.Result<MessagingGuide>("get_messaging_guide");
        LeadSummary lead = turn.Ctx<LeadSummary>("lead");
        Owner owner = turn.Ctx<Owner>("owner") ?? new Owner(null, null);
        int version = turn.Ctx<int>("version");
        string relationship = turn.Ctx<string>("relationship") ?? "net_new";
        string history = turn.Ctx<string>("history") ?? "";
        Fact[] facts = turn.Ctx<Fact[]>("facts") ?? [];

        string first = FirstName(lead.Name);
        string rep = FirstName(owner.Name);
        string company = lead.Company.Length > 0 ? lead.Company : "your team";
        string shortCompany = company.Split(' ')[0];

        (string subject, string body) = relationship switch
        {
            "existing_customer" => Expansion(lead, company, facts, turn.Ctx<string[]>("historyNotes") ?? [], guide),
            "former_opportunity" => Reengage(company, shortCompany, facts, history, guide),
            _ => NetNew(lead, company, shortCompany, facts, guide, version),
        };

        return Say($"Subject: {subject}\n\nHi {first},\n\n{body}\n\n{rep}");
    }

    private static (string, string) NetNew(LeadSummary lead, string company, string shortCompany, Fact[] facts, MessagingGuide guide, int version)
    {
        var body = new StringBuilder();
        string? funding = Capture(facts, FundingPattern());
        string? hiring = Capture(facts, HiringPattern());
        bool netsuite = facts.Any(f => f.Text.Contains("NetSuite"));
        bool logistics = facts.Any(f => f.Text.Contains("logistics company", StringComparison.OrdinalIgnoreCase));

        body.Append(lead.Source.Contains("demo request", StringComparison.OrdinalIgnoreCase) ? "Thanks for requesting a demo. " : "Thanks for reaching out. ");
        if (funding is not null) body.Append($"Congratulations on the {funding}. ");
        if (hiring is not null) body.Append($"I also saw you're hiring {hiring}, which usually means invoice volume is outgrowing the team.");
        body.Append("\n\n");

        body.Append(logistics
            ? "Fernway automates carrier invoice matching and reconciliation"
            : "Fernway automates AP and finance operations workflows");
        body.Append(netsuite ? " and plugs straight into NetSuite. " : " and connects to the systems you already use. ");

        if (logistics && version == 1)
        {
            body.Append("Teams like yours typically cut invoice processing time by 70% in the first quarter.");
        }
        else if (logistics && Proof(guide, "P-1") is { } proof)
        {
            body.Append(proof);
        }
        body.Append("\n\n");

        body.Append(lead.Message.Contains("peak season", StringComparison.OrdinalIgnoreCase)
            ? $"Would a short call next week help you see whether this fits {shortCompany}'s peak-season plans?"
            : $"Would a short call next week be useful to see whether this fits {shortCompany}?");

        string subject = hiring is not null || logistics ? $"Carrier invoices at {company}" : $"{company} and Fernway";
        return (subject, body.ToString().Trim());
    }

    private static (string, string) Expansion(LeadSummary lead, string company, Fact[] facts, string[] notes, MessagingGuide guide)
    {
        var body = new StringBuilder();
        string products = notes.Select(n => ProductsPattern().Match(n)).FirstOrDefault(m => m.Success)?.Groups[1].Value ?? "the workflows";
        bool billing = lead.Message.Contains("billing", StringComparison.OrdinalIgnoreCase);

        body.Append(lead.Source.StartsWith("Webinar", StringComparison.OrdinalIgnoreCase) ? "Thanks for the question after the webinar." : "Thanks for reaching out.");
        body.Append(lead.Message.Contains('?') ? " Short answer: yes. " : " ");
        body.Append(billing ? Proof(guide, "P-5") : Proof(guide, "P-2"));
        body.Append($" It would build on the {products} {company} already runs with us.\n\n");

        if (notes.Any(n => n.Contains("overwhelmed", StringComparison.OrdinalIgnoreCase)))
        {
            body.Append("Your last quarterly review flagged that the billing team is stretched");
            body.Append(facts.Any(f => f.Text.Contains("usage-based", StringComparison.OrdinalIgnoreCase)) ? " since the usage-based billing launch, " : ", ");
            body.Append("so the timing makes sense.\n\n");
        }

        string csm = notes.Select(n => CsmPattern().Match(n)).FirstOrDefault(m => m.Success)?.Groups[1].Value ?? "your success manager";
        body.Append($"Would you be open to a working session with {csm} and me next week to map {(billing ? "your billing workflow" : "what to automate next")}?");
        return (billing ? $"Billing operations at {company}" : $"What's next for {company} and Fernway", body.ToString());
    }

    private static (string, string) Reengage(string company, string shortCompany, Fact[] facts, string history, MessagingGuide guide)
    {
        var body = new StringBuilder();
        string? erp = Capture(facts, MigrationPattern());
        body.Append("Thanks for coming back to us");
        body.Append(erp is not null ? $", and congratulations on completing the move to {erp}. " : ". ");
        if (history.Contains("connector", StringComparison.OrdinalIgnoreCase))
        {
            body.Append("When we last spoke, the missing connector for your legacy system was the blocker.");
        }
        body.Append("\n\n");

        if (erp == "NetSuite" && Proof(guide, "P-4") is { } proof)
        {
            body.Append($"That gap is closed. {proof} Approvals and AP can run straight into your new system.\n\n");
        }
        else
        {
            body.Append($"{Proof(guide, "P-2")}\n\n");
        }

        body.Append($"Would a short call next week be useful to pick up where {shortCompany} and Fernway left off?");
        return (erp is not null ? $"{company} on {erp}: worth another look?" : $"{company} and Fernway, revisited", body.ToString());
    }

    private static string? Proof(MessagingGuide guide, string id) => guide.ProofPoints.FirstOrDefault(p => p.Id == id)?.Text;

    private static string? Capture(Fact[] facts, Regex pattern) =>
        facts.Select(f => pattern.Match(f.Text)).FirstOrDefault(m => m.Success)?.Groups[1].Value.Trim();

    [GeneratedRegex(@"raises (\$[\d.,]+[MBK]? Series [A-Z])")]
    private static partial Regex FundingPattern();

    [GeneratedRegex(@"is hiring ([^(]+?)(?: \(|$)")]
    private static partial Regex HiringPattern();

    [GeneratedRegex(@"completes its move to (\w+)")]
    private static partial Regex MigrationPattern();

    [GeneratedRegex(@"CSM: ([^.]+)\.")]
    private static partial Regex CsmPattern();

    [GeneratedRegex(@"^Owns ([^;]+);")]
    private static partial Regex ProductsPattern();
}

internal sealed class ReviewerBrain : DemoBrain
{
    public override int PlanMs => 450;

    public override int ComposeMs => 650;

    public override ChatMessage Respond(DemoTurn turn)
    {
        LeadSummary lead = turn.Ctx<LeadSummary>("lead");
        EmailDraft draft = turn.Ctx<EmailDraft>("draft");
        if (!turn.Has("check_email_policy"))
        {
            return Call(("check_email_policy", new { leadId = lead.Id, subject = draft.Subject, body = draft.Body }));
        }

        EmailPolicyResult policy = turn.Result<EmailPolicyResult>("check_email_policy");
        var issues = policy.Issues.Select(i => new ReviewIssue(i.Severity, i.Message, i.Excerpt)).ToList();
        EmailIssue[] errors = [.. policy.Issues.Where(i => i.Severity == "error")];

        string summary = errors.Length == 0
            ? $"Specific, {policy.WordCount} words, one clear ask, and every number traces back to research or an approved proof point."
            : errors.Any(e => e.Code == "unsupported_claim")
                ? "The benchmark isn't backed by anything we can cite. Replace it with an approved proof point (the Kestrel Freight result fits) or drop it."
                : "Fix the blocking issues before this can go to the rep.";

        return Json(new { passed = errors.Length == 0, issues, summary });
    }
}
