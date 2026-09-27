extern alias CrmServer;

using LeadPilot.Core.Mcp;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Policy;
using LeadPilot.Crm.Tools;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeadPilot.Tests;

public class McpTests
{
    private static readonly string[] ExpectedTools =
    [
        "list_inbound_leads", "get_lead", "enrich_company", "search_news", "find_account", "get_account_history", "get_icp_rubric",
        "score_lead", "check_contact_policy", "get_territory_owner", "get_messaging_guide", "check_email_policy", "get_crm_changes",
        "update_lead", "log_activity", "create_task", "queue_email", "add_inbound_lead",
    ];

    [Fact]
    public async Task Embedded_server_exposes_every_crm_tool_with_annotations()
    {
        await using CrmConnection crm = await CrmConnection.ConnectAsync("embedded", null);

        Assert.Equal(ExpectedTools.Order(), crm.Tools.Select(t => t.Name).Order());
        Assert.True(crm.Tool("get_lead").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.NotEqual(true, crm.Tool("queue_email").ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task Queue_email_is_refused_server_side_when_policy_fails()
    {
        await using CrmConnection crm = await CrmConnection.ConnectAsync("embedded", null);

        var blocked = await Assert.ThrowsAsync<CrmToolException>(() => crm.CallAsync<WriteResult>("queue_email", Args(
            ("leadId", "L-205"), ("fromRepId", "R-AR"), ("subject", "Hello"), ("body", "Hi Ethan, Brightmark question?"))));
        Assert.Contains("contact policy", blocked.Message);

        var invented = await Assert.ThrowsAsync<CrmToolException>(() => crm.CallAsync<WriteResult>("queue_email", Args(
            ("leadId", "L-201"), ("fromRepId", "R-JB"), ("subject", "Hello"), ("body", "Harborline could save 70%. Chat?"))));
        Assert.Contains("\"70\"", invented.Message);

        (CrmChange[] changes, _) = await crm.CallAsync<CrmChange[]>("get_crm_changes", Args());
        Assert.Empty(changes);
    }

    [Fact]
    public async Task Update_lead_validates_status_and_rep()
    {
        await using CrmConnection crm = await CrmConnection.ConnectAsync("embedded", null);

        await Assert.ThrowsAsync<CrmToolException>(() => crm.CallAsync<WriteResult>("update_lead", Args(("leadId", "L-201"), ("status", "Hot"))));
        await Assert.ThrowsAsync<CrmToolException>(() => crm.CallAsync<WriteResult>("update_lead", Args(("leadId", "L-201"), ("status", "Working"), ("ownerId", "R-XX"))));

        (WriteResult ok, _) = await crm.CallAsync<WriteResult>("update_lead", Args(("leadId", "L-201"), ("status", "Working"), ("ownerId", "R-JB")));
        (LeadSummary lead, _) = await crm.CallAsync<LeadSummary>("get_lead", Args(("leadId", "L-201")));
        Assert.True(ok.Ok);
        Assert.Equal("Working", lead.Status);
        Assert.Equal("Jordan Blake", lead.OwnerName);
    }

    [Fact]
    public async Task Each_connection_gets_its_own_crm_sandbox()
    {
        await using CrmConnection a = await CrmConnection.ConnectAsync("embedded", null);
        await using CrmConnection b = await CrmConnection.ConnectAsync("embedded", null);

        await a.CallAsync<WriteResult>("update_lead", Args(("leadId", "L-201"), ("status", "Disqualified")));

        Assert.Equal("Disqualified", (await a.CallAsync<LeadSummary>("get_lead", Args(("leadId", "L-201")))).Value.Status);
        Assert.Equal("New", (await b.CallAsync<LeadSummary>("get_lead", Args(("leadId", "L-201")))).Value.Status);
    }

    [Fact]
    public async Task Standalone_server_works_over_streamable_http_with_per_session_sandboxes()
    {
        await using var server = new WebApplicationFactory<CrmServer::Program>();
        HttpClient http = server.CreateClient();
        var endpoint = new Uri(http.BaseAddress!, "/mcp").ToString();

        await using CrmConnection a = await CrmConnection.ConnectAsync("http", endpoint, http);
        await using CrmConnection b = await CrmConnection.ConnectAsync("http", endpoint, http);

        Assert.Equal(ExpectedTools.Length, a.Tools.Count);
        (FitScore fit, _) = await a.CallAsync<FitScore>("score_lead", Args(("leadId", "L-201")));
        Assert.Equal(97, fit.Score);

        await a.CallAsync<WriteResult>("update_lead", Args(("leadId", "L-204"), ("status", "Disqualified")));
        Assert.Equal("Disqualified", (await a.CallAsync<LeadSummary>("get_lead", Args(("leadId", "L-204")))).Value.Status);
        Assert.Equal("New", (await b.CallAsync<LeadSummary>("get_lead", Args(("leadId", "L-204")))).Value.Status);
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
}
