using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeadPilot.Tests;

public class ApiTests : IClassFixture<ApiTests.Factory>
{
    private readonly HttpClient _http;

    public ApiTests(Factory factory) => _http = factory.CreateClient();

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("LeadPilot:DemoPace", "0").UseSetting("LeadPilot:Mode", "demo");
    }

    [Fact]
    public async Task Health_agents_and_graph_describe_the_system()
    {
        JsonElement health = await _http.GetFromJsonAsync<JsonElement>("/api/health");
        JsonElement agents = await _http.GetFromJsonAsync<JsonElement>("/api/agents");
        JsonElement graph = await _http.GetFromJsonAsync<JsonElement>("/api/graph");

        Assert.Equal("demo", health.GetProperty("mode").GetString());
        Assert.Equal(6, agents.GetArrayLength());
        Assert.Equal(10, graph.GetProperty("nodes").GetArrayLength());
        Assert.Contains(graph.GetProperty("edges").EnumerateArray(), e => e.GetProperty("kind").GetString() == "revise");
    }

    [Fact]
    public async Task Full_run_streams_events_pauses_for_approval_and_writes_on_approve()
    {
        JsonElement workspace = await CreateWorkspace();
        string id = workspace.GetProperty("id").GetString()!;
        Assert.Equal(6, workspace.GetProperty("leads").GetArrayLength());

        List<(string Event, JsonElement Data)> run = await Sse($"/api/workspaces/{id}/runs", new { leadId = "L-206" });
        Assert.Equal("run_started", run[0].Event);
        Assert.DoesNotContain(run, e => e.Event == "error");
        Assert.Contains(run, e => e.Event == "tool_call" && e.Data.GetProperty("tool").GetString() == "find_account");
        JsonElement approval = run.Single(e => e.Event == "approval_required").Data;
        Assert.Equal("pursue", approval.GetProperty("request").GetProperty("plan").GetProperty("decision").GetString());

        string runId = run[0].Data.GetProperty("runId").GetString()!;
        JsonElement pending = (await _http.GetFromJsonAsync<JsonElement>($"/api/workspaces/{id}")).GetProperty("pending");
        Assert.Equal(runId, pending.GetProperty("runId").GetString());

        List<(string Event, JsonElement Data)> decision = await Sse($"/api/workspaces/{id}/runs/{runId}/decision", new { approved = true });
        JsonElement summary = decision.Single(e => e.Event == "run_completed").Data.GetProperty("summary");
        Assert.True(summary.GetProperty("approved").GetBoolean());
        Assert.Equal(4, decision.Count(e => e.Event == "crm_write"));

        JsonElement after = await _http.GetFromJsonAsync<JsonElement>($"/api/workspaces/{id}");
        Assert.Equal(JsonValueKind.Null, after.GetProperty("pending").ValueKind);
        Assert.Equal(4, after.GetProperty("changes").GetArrayLength());
    }

    [Fact]
    public async Task Decision_for_an_unknown_run_is_a_conflict()
    {
        string id = (await CreateWorkspace()).GetProperty("id").GetString()!;

        HttpResponseMessage response = await _http.PostAsJsonAsync($"/api/workspaces/{id}/runs/run_nope/decision", new { approved = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Leads_can_be_added_and_are_validated()
    {
        string id = (await CreateWorkspace()).GetProperty("id").GetString()!;

        HttpResponseMessage bad = await _http.PostAsJsonAsync($"/api/workspaces/{id}/leads",
            new { name = "X", title = "CFO", email = "not-an-email", company = "Acme", country = "US", source = "Web" });
        HttpResponseMessage good = await _http.PostAsJsonAsync($"/api/workspaces/{id}/leads",
            new { name = "Lena Fox", title = "CFO", email = "lena@acme-logistics.com", company = "Acme Logistics", country = "us", source = "Web form", marketingConsent = true });

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        JsonElement lead = await good.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("US", lead.GetProperty("country").GetString());
        Assert.Equal(7, (await _http.GetFromJsonAsync<JsonElement>($"/api/workspaces/{id}")).GetProperty("leads").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_workspace_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/workspaces/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsJsonAsync("/api/workspaces/nope/runs", new { leadId = "L-201" })).StatusCode);
    }

    private async Task<JsonElement> CreateWorkspace()
    {
        HttpResponseMessage response = await _http.PostAsync("/api/workspaces", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<(string Event, JsonElement Data)>> Sse(string url, object body)
    {
        HttpResponseMessage response = await _http.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = new List<(string, JsonElement)>();
        string? name = null;
        foreach (string line in (await response.Content.ReadAsStringAsync()).Split('\n'))
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal)) name = line[7..].Trim();
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && name is not null) events.Add((name, JsonDocument.Parse(line[6..]).RootElement.Clone()));
        }
        return events;
    }
}
