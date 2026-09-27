namespace LeadPilot.Crm.Domain;

public sealed record Rep(string Id, string Name, string Title, string Email, string Territory);

public sealed class InboundLead
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Email { get; init; }
    public required string Company { get; init; }
    public required string Domain { get; init; }
    public required string Country { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
    public bool MarketingConsent { get; init; }
    public DateOnly ReceivedOn { get; init; }

    public string Status { get; set; } = LeadStatus.New;
    public int? Score { get; set; }
    public string? Tier { get; set; }
    public string? OwnerId { get; set; }
    public string? AccountId { get; set; }
    public string? Note { get; set; }

    public string FirstName => Name.Split(' ')[0];
}

public static class LeadStatus
{
    public const string New = "New";
    public const string Working = "Working";
    public const string Nurture = "Nurture";
    public const string Disqualified = "Disqualified";
    public const string DoNotContact = "Do not contact";
    public const string NeedsReview = "Needs review";

    public static readonly string[] All = [New, Working, Nurture, Disqualified, DoNotContact, NeedsReview];
}

public sealed record Account(
    string Id,
    string Name,
    string Domain,
    string Type,
    string Industry,
    string OwnerId,
    string? CsmName,
    string[] Products,
    decimal? Arr,
    DateOnly? CustomerSince,
    DateOnly? RenewalDate);

public sealed record Contact(string Id, string AccountId, string Name, string Title, string Email);

public sealed record Opportunity(
    string Id,
    string AccountId,
    string Name,
    string Stage,
    decimal Amount,
    string Currency,
    DateOnly CloseDate,
    string OwnerId,
    string? LossReason);

public sealed record Activity(string Id, DateOnly On, string Type, string Subject, string Summary, string? AccountId, string? LeadId, string By);

public sealed record CrmTask(string Id, string OwnerId, string Title, DateOnly Due, string? LeadId, string? AccountId);

public sealed record OutboxEmail(string Id, string LeadId, string From, string To, string Subject, string Body, string Status);

public sealed record CrmChange(int Seq, string Type, string Target, string Summary);

public sealed record CompanyProfile(
    string Domain,
    string Name,
    string Industry,
    string Description,
    string Headquarters,
    string Country,
    int Employees,
    int? RevenueUsdMillions,
    string[] TechStack);

public sealed record NewsItem(string Id, string Domain, DateOnly Date, string Headline, string Summary, string Source);

public sealed record ProofPoint(string Id, string Text, string? Industry);

public sealed record DoNotContactEntry(string Email, DateOnly Since, string Reason);
