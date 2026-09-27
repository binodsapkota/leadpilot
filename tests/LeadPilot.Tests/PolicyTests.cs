using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Policy;

namespace LeadPilot.Tests;

public class PolicyTests
{
    private static readonly CrmData Data = SeedData.Create();

    [Theory]
    [InlineData("L-201", 97, "A")]
    [InlineData("L-202", 80, "A")]
    [InlineData("L-203", 78, "A")]
    [InlineData("L-204", 10, "D")]
    [InlineData("L-205", 70, "B")]
    [InlineData("L-206", 93, "A")]
    public void Fit_score_is_deterministic(string leadId, int score, string tier)
    {
        InboundLead lead = Data.Lead(leadId)!;
        FitScore fit = FitScoring.Score(lead, CrmData.Company(lead.Domain), SeedData.News.Where(n => n.Domain == lead.Domain));

        Assert.Equal(score, fit.Score);
        Assert.Equal(tier, fit.Tier);
        Assert.Equal(fit.Score, fit.Breakdown.Sum(l => l.Points));
    }

    [Theory]
    [InlineData("L-201", "clear")]
    [InlineData("L-203", "consent_required")]
    [InlineData("L-204", "clear")]
    [InlineData("L-205", "blocked")]
    public void Contact_policy_status(string leadId, string status)
    {
        Assert.Equal(status, ContactPolicy.Check(Data, Data.Lead(leadId)!).Status);
    }

    [Fact]
    public void Personal_email_is_flagged_and_competitors_are_blocked()
    {
        Assert.Contains(ContactPolicy.Check(Data, Data.Lead("L-204")!).Reasons, r => r.Contains("Personal email"));

        var competitor = new InboundLead { Id = "L-900", Name = "Pat Spy", Title = "PM", Email = "pat@flowbolt.com", Company = "Flowbolt", Domain = "flowbolt.com", Country = "US", Source = "Contact form", Message = "" };
        Assert.Equal("blocked", ContactPolicy.Check(Data, competitor).Status);
    }

    [Theory]
    [InlineData("US", 1200, "R-JB")]
    [InlineData("US", 2600, "R-AR")]
    [InlineData("DE", 450, "R-LW")]
    [InlineData("GB", 5000, "R-LW")]
    public void Territory_routing(string country, int employees, string repId)
    {
        Assert.Equal(repId, Territories.OwnerFor(country, employees).Id);
    }

    [Fact]
    public void Email_policy_rejects_numbers_that_are_not_backed_by_research()
    {
        InboundLead lead = Data.Lead("L-201")!;

        EmailPolicyResult invented = EmailPolicy.Check(lead, "Hi", "Harborline teams cut costs by 70%. Want to chat?");
        EmailPolicyResult backed = EmailPolicy.Check(lead, "Hi", "Congrats on the $85M Series C at Harborline. Kestrel went from 6 days to 1 day. Want to chat?");

        Assert.False(invented.Passed);
        Assert.Contains(invented.Issues, i => i.Code == "unsupported_claim" && i.Excerpt!.Contains("70%"));
        Assert.True(backed.Passed);
    }

    [Fact]
    public void Email_policy_checks_length_personalization_and_banned_phrases()
    {
        InboundLead lead = Data.Lead("L-201")!;
        string longBody = string.Join(' ', Enumerable.Repeat("word", 160)) + " Harborline?";

        Assert.Contains(EmailPolicy.Check(lead, "Hi", longBody).Issues, i => i.Code == "too_long");
        Assert.Contains(EmailPolicy.Check(lead, "Hi", "A generic note. Want to chat?").Issues, i => i.Code == "no_personalization");
        Assert.Contains(EmailPolicy.Check(lead, "Hi", "Harborline gets a guarantee. Chat?").Issues, i => i.Code == "banned_phrase");
        Assert.Contains(EmailPolicy.Check(lead, "Hi", "Harborline, let's talk.").Issues, i => i.Code == "no_cta" && i.Severity == "warning");
    }
}
