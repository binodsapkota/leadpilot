using System.Globalization;
using System.Text.RegularExpressions;
using LeadPilot.Crm.Domain;

namespace LeadPilot.Crm.Policy;

public sealed record ScoreLine(string Criterion, int Points, int Max, string Detail);

public sealed record FitScore(string LeadId, int Score, string Tier, ScoreLine[] Breakdown);

public sealed record ContactPolicyResult(string LeadId, string Status, string[] Reasons, string[] AllowedChannels);

public sealed record EmailIssue(string Code, string Severity, string Message, string? Excerpt);

public sealed record EmailPolicyResult(bool Passed, int WordCount, EmailIssue[] Issues);

public static class Regions
{
    private static readonly HashSet<string> NorthAmerica = ["US", "CA"];
    private static readonly HashSet<string> Europe = ["AT", "BE", "DE", "DK", "ES", "FI", "FR", "IE", "IT", "NL", "PL", "PT", "SE"];

    public static string Of(string country) =>
        NorthAmerica.Contains(country) ? "NA" : country == "GB" ? "UK" : Europe.Contains(country) ? "EU" : "Other";

    public static bool IsGdpr(string country) => Of(country) is "EU" or "UK";
}

/// <summary>The ideal customer profile, scored in code so every agent and every run gets the same number.</summary>
public static class FitScoring
{
    public const string Rubric =
        "Company size 200-5,000 employees (25) | Core industry: logistics, fintech, healthcare, SaaS, manufacturing (20) | " +
        "Region: North America (10), UK/EU (8) | Stack: NetSuite (15), SAP or Salesforce (10) | " +
        "Buying signals in the last 90 days, 5 each, max 15: demo request, funding, hiring finance/ops roles, new finance leader, ERP migration | " +
        "Contact seniority: C-level (15), VP (12), Director or Head (10), Manager (6). Tiers: A >= 75, B >= 55, C >= 35, D below 35.";

    private static readonly HashSet<string> CoreIndustries = ["logistics", "fintech", "healthcare", "saas", "manufacturing"];

    public static FitScore Score(InboundLead lead, CompanyProfile? company, IEnumerable<NewsItem> news)
    {
        var lines = new List<ScoreLine>();

        if (company is null)
        {
            lines.Add(new("Company size", 0, 25, "No company profile (personal email or unknown domain)"));
            lines.Add(new("Industry", 0, 20, "Unknown"));
        }
        else
        {
            int size = company.Employees is >= 200 and <= 5000 ? 25 : company.Employees is >= 50 and <= 20000 ? 10 : 0;
            lines.Add(new("Company size", size, 25, $"{company.Employees:N0} employees"));
            int industry = CoreIndustries.Contains(company.Industry) ? 20 : company.Industry == "retail" ? 10 : 0;
            lines.Add(new("Industry", industry, 20, company.Industry));
        }

        string region = Regions.Of(lead.Country);
        lines.Add(new("Region", region switch { "NA" => 10, "UK" or "EU" => 8, _ => 0 }, 10, $"{lead.Country} ({region})"));

        string[] stack = company?.TechStack ?? [];
        int stackPoints = stack.Contains("NetSuite") ? 15 : stack.Any(s => s is "SAP" or "Salesforce") ? 10 : 0;
        lines.Add(new("Tech stack", stackPoints, 15, stack.Length > 0 ? string.Join(", ", stack) : "Unknown"));

        var signals = new List<string>();
        if (lead.Source.Contains("demo request", StringComparison.OrdinalIgnoreCase)) signals.Add("demo request");
        foreach (NewsItem item in news.Where(n => n.Date >= SeedData.Today.AddDays(-90)))
        {
            string h = item.Headline.ToLowerInvariant();
            if (h.Contains("raises") || h.Contains("series")) signals.Add("funding");
            else if (h.Contains("hiring")) signals.Add("hiring finance/ops roles");
            else if (h.Contains("cfo") || h.Contains("names a new")) signals.Add("new finance leader");
            else if (h.Contains("move to") || h.Contains("migrat")) signals.Add("ERP migration");
        }
        signals = [.. signals.Distinct()];
        lines.Add(new("Buying signals", Math.Min(15, signals.Count * 5), 15, signals.Count > 0 ? string.Join(", ", signals) : "None found"));

        string title = lead.Title.ToLowerInvariant();
        int seniority =
            Regex.IsMatch(title, @"\b(chief|ceo|cfo|coo|cto|founder)\b") ? 15 :
            Regex.IsMatch(title, @"\b(vp|vice president)\b") ? 12 :
            Regex.IsMatch(title, @"\b(director|head)\b") ? 10 :
            title.Contains("manager") ? 6 : 0;
        lines.Add(new("Contact seniority", seniority, 15, lead.Title));

        int score = lines.Sum(l => l.Points);
        string tier = score >= 75 ? "A" : score >= 55 ? "B" : score >= 35 ? "C" : "D";
        return new FitScore(lead.Id, score, tier, [.. lines]);
    }
}

public static class Territories
{
    public static Rep OwnerFor(string country, int employees) =>
        CrmData.Rep(Regions.Of(country) switch
        {
            "NA" when employees >= 2000 => "R-AR",
            "NA" => "R-JB",
            "UK" or "EU" => "R-LW",
            _ => "R-JB",
        })!;
}

/// <summary>Who may be contacted, and how. Enforced again server-side when an email is queued.</summary>
public static class ContactPolicy
{
    public static ContactPolicyResult Check(CrmData data, InboundLead lead)
    {
        string domain = lead.Email.Split('@').Last().ToLowerInvariant();
        var reasons = new List<string>();

        if (data.DoNotContact.FirstOrDefault(d => d.Email.Equals(lead.Email, StringComparison.OrdinalIgnoreCase)) is { } dnc)
        {
            reasons.Add($"{dnc.Reason} on {dnc.Since:yyyy-MM-dd}.");
            return new(lead.Id, "blocked", [.. reasons], []);
        }

        if (SeedData.CompetitorDomains.Contains(domain))
        {
            return new(lead.Id, "blocked", ["Email domain belongs to a competitor."], []);
        }

        if (SeedData.PersonalEmailDomains.Contains(domain))
        {
            reasons.Add("Personal email address; no company can be verified.");
        }

        if (Regions.IsGdpr(lead.Country) && !lead.MarketingConsent)
        {
            reasons.Add($"GDPR: no marketing consent recorded for a contact in {lead.Country}.");
            return new(lead.Id, "consent_required", [.. reasons], ["double opt-in consent request"]);
        }

        return new(lead.Id, "clear", [.. reasons], ["email", "phone"]);
    }
}

/// <summary>
/// Outbound email rules. The key one: every number in an email must trace back to research data or an approved
/// proof point, which is how invented statistics get caught.
/// </summary>
public static partial class EmailPolicy
{
    public const int MaxWords = 150;

    public const string Guide =
        "Max 150 words. One clear call to action phrased as a question. Mention something specific about the prospect. " +
        "Every number must come from research facts or an approved proof point; never invent statistics or benchmarks. " +
        "No pricing, discounts or guarantees. Sign as the owning rep.";

    private static readonly string[] BannedPhrases = ["guarantee", "discount", "% off", "free forever", "risk-free", "act now", "limited time"];

    public static EmailPolicyResult Check(InboundLead lead, string subject, string body)
    {
        var issues = new List<EmailIssue>();
        int words = body.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

        if (words > MaxWords) issues.Add(new("too_long", "error", $"Email is {words} words; the limit is {MaxWords}.", null));
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 70) issues.Add(new("subject", "error", "Subject must be 1-70 characters.", subject));

        foreach (string phrase in BannedPhrases.Where(p => (subject + " " + body).Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new("banned_phrase", "error", $"\"{phrase}\" is not allowed in outbound email.", SentenceWith(body, phrase)));
        }

        HashSet<string> supported = SupportedNumbers(lead);
        foreach (Match m in NumberPattern().Matches(body))
        {
            string normalized = Normalize(m.Value);
            if (!supported.Contains(normalized))
            {
                issues.Add(new("unsupported_claim", "error",
                    $"\"{m.Value.Trim()}\" does not appear in the research or approved proof points.", SentenceWith(body, m.Value)));
            }
        }

        if (lead.Company.Length > 0 && !body.Contains(lead.Company.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("no_personalization", "error", $"Email never mentions {lead.Company}.", null));
        }

        if (!body.Contains('?')) issues.Add(new("no_cta", "warning", "No question-style call to action.", null));

        return new EmailPolicyResult(!issues.Any(i => i.Severity == "error"), words, [.. issues]);
    }

    private static HashSet<string> SupportedNumbers(InboundLead lead)
    {
        CompanyProfile? company = CrmData.Company(lead.Domain);
        var corpus = new List<string> { lead.Message, lead.Company };
        if (company is not null)
        {
            corpus.AddRange([company.Description, company.Headquarters, company.Employees.ToString(CultureInfo.InvariantCulture)]);
            if (company.RevenueUsdMillions is { } revenue) corpus.Add(revenue.ToString(CultureInfo.InvariantCulture));
        }
        corpus.AddRange(SeedData.News.Where(n => n.Domain == lead.Domain).SelectMany(n => new[] { n.Headline, n.Summary }));
        corpus.AddRange(SeedData.ProofPoints.Select(p => p.Text));

        return [.. corpus.SelectMany(text => NumberPattern().Matches(text).Select(m => Normalize(m.Value)))];
    }

    private static string Normalize(string number) => number.Trim().TrimEnd('.', ',').Replace(",", "");

    private static string? SentenceWith(string body, string fragment)
    {
        int at = body.IndexOf(fragment, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        int start = body.LastIndexOfAny(['.', '!', '?', '\n'], Math.Max(0, at - 1)) + 1;
        int end = body.IndexOfAny(['.', '!', '?', '\n'], at);
        return body[start..(end < 0 ? body.Length : end + 1)].Trim();
    }

    [GeneratedRegex(@"\d[\d,]*(\.\d+)?")]
    private static partial Regex NumberPattern();
}
