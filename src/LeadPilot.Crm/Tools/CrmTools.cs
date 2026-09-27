using System.ComponentModel;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Policy;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LeadPilot.Crm.Tools;

public sealed record LeadSummary(
    string Id, string Name, string Title, string Email, string Company, string Domain, string Country, string Source,
    string Message, bool MarketingConsent, DateOnly ReceivedOn, string Status, int? Score, string? Tier, string? OwnerId, string? OwnerName);

public sealed record CompanyLookup(bool Found, CompanyProfile? Company);

public sealed record AccountMatch(bool Found, Account? Account, string? OwnerName);

public sealed record AccountHistory(Account Account, string OwnerName, Contact[] Contacts, Opportunity[] Opportunities, Activity[] Activities);

public sealed record MessagingGuide(string Rules, ProofPoint[] ProofPoints);

public sealed record WriteResult(bool Ok, string Id, string Message);

/// <summary>
/// The CRM, enrichment and policy tools, exposed over the Model Context Protocol. Agents only ever reach CRM data
/// through these tools, whether the server runs in-process or as its own service.
/// </summary>
[McpServerToolType]
public sealed class CrmTools(CrmWorkspaces workspaces)
{
    private CrmData Crm(McpServer server) => workspaces.Get(server.SessionId);

    [McpServerTool(Name = "list_inbound_leads", ReadOnly = true), Description("List inbound leads with their current CRM status.")]
    public LeadSummary[] ListInboundLeads(McpServer server)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate) return [.. crm.Leads.Select(Summarize)];
    }

    [McpServerTool(Name = "get_lead", ReadOnly = true), Description("Get one inbound lead, including the form message and consent flag.")]
    public LeadSummary GetLead(McpServer server, [Description("Lead id, e.g. L-201")] string leadId)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate) return Summarize(RequireLead(crm, leadId));
    }

    [McpServerTool(Name = "enrich_company", ReadOnly = true, OpenWorld = true),
     Description("Firmographics for a company domain: industry, size, revenue, headquarters and known tech stack.")]
    public CompanyLookup EnrichCompany([Description("Company web domain, e.g. harborline.com")] string domain) =>
        CrmData.Company(domain) is { } company ? new(true, company) : new(false, null);

    [McpServerTool(Name = "search_news", ReadOnly = true, OpenWorld = true),
     Description("Recent news, funding and job posts for a company domain, newest first.")]
    public NewsItem[] SearchNews([Description("Company web domain")] string domain) =>
        [.. SeedData.News.Where(n => n.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase)).OrderByDescending(n => n.Date)];

    [McpServerTool(Name = "find_account", ReadOnly = true), Description("Find an existing CRM account by company domain.")]
    public AccountMatch FindAccount(McpServer server, [Description("Company web domain")] string domain)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate)
        {
            Account? account = crm.Accounts.FirstOrDefault(a => a.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));
            return account is null ? new(false, null, null) : new(true, account, CrmData.Rep(account.OwnerId)?.Name);
        }
    }

    [McpServerTool(Name = "get_account_history", ReadOnly = true),
     Description("Contacts, opportunities (including closed-lost reasons) and recent activities for an account.")]
    public AccountHistory GetAccountHistory(McpServer server, [Description("Account id, e.g. A-310")] string accountId)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate)
        {
            Account account = crm.Accounts.FirstOrDefault(a => a.Id.Equals(accountId, StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException($"No account {accountId}.");
            return new(
                account,
                CrmData.Rep(account.OwnerId)?.Name ?? account.OwnerId,
                [.. crm.Contacts.Where(c => c.AccountId == account.Id)],
                [.. crm.Opportunities.Where(o => o.AccountId == account.Id)],
                [.. crm.Activities.Where(a => a.AccountId == account.Id).OrderByDescending(a => a.On)]);
        }
    }

    [McpServerTool(Name = "get_icp_rubric", ReadOnly = true), Description("The ideal customer profile scoring rubric.")]
    public string GetIcpRubric() => FitScoring.Rubric;

    [McpServerTool(Name = "score_lead", ReadOnly = true),
     Description("Score a lead 0-100 against the ideal customer profile. Returns the tier and a per-criterion breakdown.")]
    public FitScore ScoreLead(McpServer server, [Description("Lead id")] string leadId)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate)
        {
            InboundLead lead = RequireLead(crm, leadId);
            return FitScoring.Score(lead, CrmData.Company(lead.Domain), SeedData.News.Where(n => n.Domain == lead.Domain));
        }
    }

    [McpServerTool(Name = "check_contact_policy", ReadOnly = true),
     Description("Check do-not-contact list, competitor domains and GDPR consent. Status is clear, consent_required or blocked.")]
    public ContactPolicyResult CheckContactPolicy(McpServer server, [Description("Lead id")] string leadId)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate) return ContactPolicy.Check(crm, RequireLead(crm, leadId));
    }

    [McpServerTool(Name = "get_territory_owner", ReadOnly = true), Description("The sales rep who owns net-new leads for a country and company size.")]
    public Rep GetTerritoryOwner([Description("ISO country code, e.g. US")] string country, [Description("Employee count")] int employees) =>
        Territories.OwnerFor(country, employees);

    [McpServerTool(Name = "get_messaging_guide", ReadOnly = true), Description("Outbound email rules and the approved proof points that may be quoted.")]
    public MessagingGuide GetMessagingGuide() => new(EmailPolicy.Guide, SeedData.ProofPoints);

    [McpServerTool(Name = "check_email_policy", ReadOnly = true),
     Description("Lint a draft email: length, banned phrases, personalization, and whether every number is backed by research or a proof point.")]
    public EmailPolicyResult CheckEmailPolicy(McpServer server, [Description("Lead id")] string leadId, [Description("Subject line")] string subject, [Description("Email body")] string body)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate) return EmailPolicy.Check(RequireLead(crm, leadId), subject, body);
    }

    [McpServerTool(Name = "update_lead", Destructive = false, Idempotent = true),
     Description("Set a lead's status, owner, score and notes.")]
    public WriteResult UpdateLead(
        McpServer server,
        [Description("Lead id")] string leadId,
        [Description("New, Working, Nurture, Disqualified, Do not contact, or Needs review")] string status,
        [Description("Owning rep id")] string? ownerId = null,
        [Description("Fit score 0-100")] int? score = null,
        [Description("Fit tier A-D")] string? tier = null,
        [Description("Existing account id to link")] string? accountId = null,
        [Description("Short note")] string? note = null)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate)
        {
            InboundLead lead = RequireLead(crm, leadId);
            if (!LeadStatus.All.Contains(status)) throw new McpException($"Unknown status '{status}'. Use one of: {string.Join(", ", LeadStatus.All)}.");
            if (ownerId is not null && CrmData.Rep(ownerId) is null) throw new McpException($"Unknown rep '{ownerId}'.");

            lead.Status = status;
            lead.OwnerId = ownerId ?? lead.OwnerId;
            lead.Score = score ?? lead.Score;
            lead.Tier = tier ?? lead.Tier;
            lead.AccountId = accountId ?? lead.AccountId;
            lead.Note = note ?? lead.Note;

            string owner = lead.OwnerId is null ? "" : $", owner {CrmData.Rep(lead.OwnerId)!.Name}";
            string fit = lead.Score is null ? "" : $", fit {lead.Score} ({lead.Tier})";
            crm.Record("lead_updated", lead.Id, $"{lead.Name}: status {status}{owner}{fit}");
            return new(true, lead.Id, $"Lead {lead.Id} updated.");
        }
    }

    [McpServerTool(Name = "log_activity"), Description("Log an activity (note, email, call) on a lead's timeline.")]
    public WriteResult LogActivity(McpServer server, [Description("Lead id")] string leadId, [Description("Note, Email, Call or Meeting")] string type,
        [Description("Short subject")] string subject, [Description("Summary")] string summary)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate)
        {
            InboundLead lead = RequireLead(crm, leadId);
            var activity = new Activity($"ACT-{8000 + crm.NextId()}", SeedData.Today, type, subject, summary, lead.AccountId, lead.Id, "LeadPilot");
            crm.Activities.Add(activity);
            crm.Record("activity_logged", lead.Id, $"{type}: {subject}");
            return new(true, activity.Id, "Activity logged.");
        }
    }

    [McpServerTool(Name = "create_task"), Description("Create a follow-up task for a rep.")]
    public WriteResult CreateTask(McpServer server, [Description("Lead id")] string leadId, [Description("Rep id")] string ownerId,
        [Description("Task title")] string title, [Description("Due in N days")] int dueInDays = 2)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate)
        {
            InboundLead lead = RequireLead(crm, leadId);
            Rep rep = CrmData.Rep(ownerId) ?? throw new McpException($"Unknown rep '{ownerId}'.");
            var task = new CrmTask($"T-{4000 + crm.NextId()}", rep.Id, title, SeedData.Today.AddDays(Math.Clamp(dueInDays, 0, 30)), lead.Id, lead.AccountId);
            crm.Tasks.Add(task);
            crm.Record("task_created", lead.Id, $"{rep.Name}: {title} (due {task.Due:MMM d})");
            return new(true, task.Id, "Task created.");
        }
    }

    [McpServerTool(Name = "queue_email"),
     Description("Queue an outbound email from a rep to the lead. Refused if the contact policy or email policy fails. Nothing is actually sent.")]
    public WriteResult QueueEmail(McpServer server, [Description("Lead id")] string leadId, [Description("Sending rep id")] string fromRepId,
        [Description("Subject line")] string subject, [Description("Email body")] string body)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate)
        {
            InboundLead lead = RequireLead(crm, leadId);
            Rep rep = CrmData.Rep(fromRepId) ?? throw new McpException($"Unknown rep '{fromRepId}'.");

            ContactPolicyResult contact = ContactPolicy.Check(crm, lead);
            if (contact.Status != "clear") throw new McpException($"Refused: contact policy is '{contact.Status}'. {string.Join(" ", contact.Reasons)}");

            EmailPolicyResult lint = EmailPolicy.Check(lead, subject, body);
            if (!lint.Passed) throw new McpException("Refused: " + string.Join(" ", lint.Issues.Where(i => i.Severity == "error").Select(i => i.Message)));

            var email = new OutboxEmail($"E-{6000 + crm.NextId()}", lead.Id, rep.Email, lead.Email, subject, body, "Queued (demo outbox, not sent)");
            crm.Outbox.Add(email);
            crm.Record("email_queued", lead.Id, $"From {rep.Name} to {lead.Email}: \"{subject}\"");
            return new(true, email.Id, "Email queued in the outbox.");
        }
    }

    [McpServerTool(Name = "add_inbound_lead"), Description("Add a new inbound lead, as if submitted through a web form.")]
    public LeadSummary AddInboundLead(McpServer server, string name, string title, string email, string company, string country, string source,
        string message = "", bool marketingConsent = true)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate)
        {
            if (!email.Contains('@')) throw new McpException("A valid email address is required.");
            var lead = new InboundLead
            {
                Id = $"L-{300 + crm.NextId()}", Name = name.Trim(), Title = title.Trim(), Email = email.Trim().ToLowerInvariant(),
                Company = company.Trim(), Domain = email.Split('@')[1].Trim().ToLowerInvariant(), Country = country.Trim().ToUpperInvariant(),
                Source = source.Trim(), Message = message.Trim(), MarketingConsent = marketingConsent, ReceivedOn = SeedData.Today,
            };
            crm.Leads.Insert(0, lead);
            crm.Record("lead_created", lead.Id, $"{lead.Name} ({lead.Company}) via {lead.Source}");
            return Summarize(lead);
        }
    }

    [McpServerTool(Name = "get_crm_changes", ReadOnly = true), Description("Every change written to this CRM workspace, oldest first.")]
    public CrmChange[] GetCrmChanges(McpServer server)
    {
        CrmData crm = Crm(server);
        lock (crm.Gate) return [.. crm.Changes];
    }

    private static InboundLead RequireLead(CrmData crm, string leadId) => crm.Lead(leadId) ?? throw new McpException($"No lead {leadId}.");

    private static LeadSummary Summarize(InboundLead l) => new(
        l.Id, l.Name, l.Title, l.Email, l.Company, l.Domain, l.Country, l.Source, l.Message, l.MarketingConsent, l.ReceivedOn,
        l.Status, l.Score, l.Tier, l.OwnerId, l.OwnerId is null ? null : CrmData.Rep(l.OwnerId)?.Name);
}
