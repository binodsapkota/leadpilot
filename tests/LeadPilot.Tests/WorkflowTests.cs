using LeadPilot.Core.Runtime;
using LeadPilot.Core.Workflow;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Tools;

namespace LeadPilot.Tests;

public class WorkflowTests
{
    private static readonly string[] Branches = [Nodes.Research, Nodes.History, Nodes.Fit, Nodes.Compliance];

    [Fact]
    public async Task Harborline_is_pursued_after_the_reviewer_catches_an_invented_statistic()
    {
        await using RunHarness h = await RunHarness.CreateAsync();

        List<StreamEvent> events = await h.RunAsync("L-201");

        AssertNoErrors(events);
        ApprovalRequest approval = events.Approval();
        Assert.Equal(Decisions.Pursue, approval.Plan.Decision);
        Assert.Equal("R-JB", approval.Plan.OwnerId);
        Assert.Equal(97, approval.Fit.Score);
        Assert.Equal("A", approval.Fit.Tier);

        ReviewResult[] reviews = Reviews(events);
        Assert.Equal(2, reviews.Length);
        Assert.False(reviews[0].Passed);
        Assert.Contains(reviews[0].Issues, i => i.Message.Contains("70"));
        Assert.True(reviews[1].Passed);
        Assert.Equal(2, approval.Draft!.Version);
        Assert.Contains("6 days to 1 day", approval.Draft.Body);
        Assert.DoesNotContain("70%", approval.Draft.Body);
        Assert.Equal(4, approval.ProposedChanges.Length);
    }

    [Fact]
    public async Task Approving_writes_exactly_the_planned_changes_through_mcp()
    {
        await using RunHarness h = await RunHarness.CreateAsync();
        await h.RunAsync("L-201");

        List<StreamEvent> events = await h.DecideAsync(new ApprovalDecision(true));

        AssertNoErrors(events);
        RunSummary summary = events.Of<RunCompletedData>().Single().Summary;
        Assert.True(summary.Approved);
        Assert.Empty(summary.Errors);
        Assert.Equal(["update_lead", "log_activity", "create_task", "queue_email"], events.Of<CrmWriteData>().Select(w => w.Tool));

        WorkspaceSnapshot snapshot = await h.Session.SnapshotAsync();
        LeadSummary lead = snapshot.Leads.Single(l => l.Id == "L-201");
        Assert.Equal("Working", lead.Status);
        Assert.Equal("R-JB", lead.OwnerId);
        Assert.Equal(97, lead.Score);
        Assert.Equal(["lead_updated", "activity_logged", "task_created", "email_queued"], snapshot.Changes.Select(c => c.Type));
        Assert.Null(h.Session.Pending);
    }

    [Fact]
    public async Task Existing_customer_stays_with_the_account_owner_and_passes_review_first_time()
    {
        await using RunHarness h = await RunHarness.CreateAsync();

        List<StreamEvent> events = await h.RunAsync("L-202");

        AssertNoErrors(events);
        ApprovalRequest approval = events.Approval();
        Assert.Equal(Decisions.Pursue, approval.Plan.Decision);
        Assert.Equal("R-AR", approval.Plan.OwnerId);
        Assert.Single(Reviews(events));
        Assert.Contains("Nina Park", approval.Draft!.Body);
        Assert.Equal("existing_customer", events.Report<HistoryReport>("history").Relationship);
    }

    [Fact]
    public async Task Missing_gdpr_consent_overrides_the_strategist_to_nurture()
    {
        await using RunHarness h = await RunHarness.CreateAsync();

        List<StreamEvent> events = await h.RunAsync("L-203");

        AssertNoErrors(events);
        ApprovalRequest approval = events.Approval();
        Assert.Equal(Decisions.Nurture, approval.Plan.Decision);
        Assert.Equal("R-NQ", approval.Plan.OwnerId);
        Assert.Contains(approval.Plan.Overrides, o => o.Contains("GDPR"));
        Assert.Null(approval.Draft);
        Assert.DoesNotContain(events.Of<NodeStartedData>(), n => n.Node == Nodes.Copywriter);

        await h.DecideAsync(new ApprovalDecision(true));
        Assert.Equal("Nurture", (await h.Session.SnapshotAsync()).Leads.Single(l => l.Id == "L-203").Status);
    }

    [Fact]
    public async Task Student_with_personal_email_is_disqualified()
    {
        await using RunHarness h = await RunHarness.CreateAsync();

        List<StreamEvent> events = await h.RunAsync("L-204");

        AssertNoErrors(events);
        Assert.Equal(Decisions.Disqualify, events.Approval().Plan.Decision);
        Assert.Equal("D", events.Approval().Fit.Tier);
        Assert.DoesNotContain(events.Of<NodeStartedData>(), n => n.Node == Nodes.Copywriter);
    }

    [Fact]
    public async Task Do_not_contact_lead_is_blocked_and_marked_in_the_crm()
    {
        await using RunHarness h = await RunHarness.CreateAsync();

        List<StreamEvent> events = await h.RunAsync("L-205");

        AssertNoErrors(events);
        Assert.Equal(Decisions.Blocked, events.Approval().Plan.Decision);

        await h.DecideAsync(new ApprovalDecision(true));
        WorkspaceSnapshot snapshot = await h.Session.SnapshotAsync();
        Assert.Equal("Do not contact", snapshot.Leads.Single(l => l.Id == "L-205").Status);
        Assert.DoesNotContain(snapshot.Changes, c => c.Type == "email_queued");
    }

    [Fact]
    public async Task Former_opportunity_goes_back_to_its_owner_with_an_angle_on_what_changed()
    {
        await using RunHarness h = await RunHarness.CreateAsync();

        List<StreamEvent> events = await h.RunAsync("L-206");

        AssertNoErrors(events);
        ApprovalRequest approval = events.Approval();
        Assert.Equal(Decisions.Pursue, approval.Plan.Decision);
        Assert.Equal("R-LW", approval.Plan.OwnerId);
        Assert.Equal("former_opportunity", events.Report<HistoryReport>("history").Relationship);
        Assert.Contains("NetSuite", approval.Draft!.Body);
        Assert.True(approval.Review!.Passed);
    }

    [Fact]
    public async Task Rejecting_flags_the_lead_for_review_and_sends_nothing()
    {
        await using RunHarness h = await RunHarness.CreateAsync();
        await h.RunAsync("L-201");

        List<StreamEvent> events = await h.DecideAsync(new ApprovalDecision(false, Note: "Dana is already talking to our partner."));

        RunSummary summary = events.Of<RunCompletedData>().Single().Summary;
        Assert.False(summary.Approved);
        WorkspaceSnapshot snapshot = await h.Session.SnapshotAsync();
        Assert.Equal("Needs review", snapshot.Leads.Single(l => l.Id == "L-201").Status);
        Assert.DoesNotContain(snapshot.Changes, c => c.Type == "email_queued");
    }

    [Fact]
    public async Task Rep_edits_are_revalidated_by_the_crm_before_queueing()
    {
        await using RunHarness h = await RunHarness.CreateAsync();
        List<StreamEvent> run = await h.RunAsync("L-201");
        string body = run.Approval().Draft!.Body + "\n\nP.S. Most teams save 40% in year one.";

        List<StreamEvent> events = await h.DecideAsync(new ApprovalDecision(true, Body: body));

        RunSummary summary = events.Of<RunCompletedData>().Single().Summary;
        Assert.Single(summary.Errors);
        Assert.Contains("\"40\"", summary.Errors[0]);
        Assert.Contains(events.Of<CrmWriteData>(), w => w.Tool == "queue_email" && !w.Ok);
        Assert.Contains(events.Of<CrmWriteData>(), w => w.Tool == "update_lead" && w.Ok);
    }

    [Fact]
    public async Task Revision_loop_is_capped_and_the_crm_still_refuses_a_failing_draft()
    {
        await using RunHarness h = await RunHarness.CreateAsync(maxDrafts: 1);

        List<StreamEvent> run = await h.RunAsync("L-201");
        ApprovalRequest approval = run.Approval();
        Assert.Equal(1, approval.Draft!.Version);
        Assert.False(approval.Review!.Passed);

        List<StreamEvent> events = await h.DecideAsync(new ApprovalDecision(true));
        Assert.Contains(events.Of<CrmWriteData>(), w => w.Tool == "queue_email" && !w.Ok);
    }

    [Fact]
    public async Task Specialist_branches_run_in_parallel()
    {
        await using var session = await PilotSession.CreateAsync("parallel", new PilotOptions { DemoPace = 0.2 }, null);
        var events = new List<StreamEvent>();
        await foreach (StreamEvent e in session.RunAsync("L-204")) events.Add(e);

        int lastBranchStart = events.FindLastIndex(e => e.Data is NodeStartedData s && Branches.Contains(s.Node));
        int firstBranchEnd = events.FindIndex(e => e.Data is NodeCompletedData c && Branches.Contains(c.Node) && c.Node != Nodes.Compliance);
        Assert.True(lastBranchStart < firstBranchEnd, "All specialist agents should start before any of them finishes.");

        int strategistStart = events.FindIndex(e => e.Data is NodeStartedData { Node: Nodes.Strategist });
        int lastBranchEnd = events.FindLastIndex(e => e.Data is NodeCompletedData c && Branches.Contains(c.Node));
        Assert.True(lastBranchEnd < strategistStart, "The strategist waits for every branch (fan-in barrier).");
    }

    [Fact]
    public async Task Starting_a_new_run_abandons_the_paused_one()
    {
        await using RunHarness h = await RunHarness.CreateAsync();
        await h.RunAsync("L-201");
        string first = h.Session.Pending!.RunId;

        await h.RunAsync("L-204");

        Assert.NotEqual(first, h.Session.Pending!.RunId);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (StreamEvent _ in h.Session.DecideAsync(first, new ApprovalDecision(true))) { }
        });
    }

    [Fact]
    public async Task Unknown_lead_reports_an_error()
    {
        await using RunHarness h = await RunHarness.CreateAsync();

        List<StreamEvent> events = await h.RunAsync("L-999");

        Assert.Contains(events.Errors(), e => e.Contains("No lead L-999"));
        Assert.Null(h.Session.Pending);
    }

    [Fact]
    public async Task New_leads_added_through_mcp_can_be_run()
    {
        await using RunHarness h = await RunHarness.CreateAsync();
        LeadSummary lead = await h.Session.AddLeadAsync(new NewLead(
            "Priya Nair", "Head of Finance", "priya@kestrelfreight.com", "Kestrel Freight", "US", "Contact form", "Interested in more automation.", true));

        List<StreamEvent> events = await h.RunAsync(lead.Id);

        AssertNoErrors(events);
        Assert.Equal("existing_customer", events.Report<HistoryReport>("history").Relationship);
        Assert.Equal("R-JB", events.Approval().Plan.OwnerId);
    }

    internal static void AssertNoErrors(List<StreamEvent> events) =>
        Assert.True(events.Errors().Length == 0, string.Join("\n", events.Errors()));

    private static ReviewResult[] Reviews(List<StreamEvent> events) =>
        [.. events.Of<ReportData>().Where(r => r.Kind == "review").Select(r => (ReviewResult)r.Data)];
}
