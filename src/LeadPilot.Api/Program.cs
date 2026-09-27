using System.Net.ServerSentEvents;
using System.Net.Mail;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using LeadPilot.Core.Agents;
using LeadPilot.Core.Runtime;
using LeadPilot.Core.Workflow;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

PilotOptions pilot = builder.Configuration.GetSection(PilotOptions.Section).Get<PilotOptions>() ?? new PilotOptions();
pilot.OpenAIApiKey ??= builder.Configuration["OPENAI_API_KEY"];
builder.Services.AddSingleton(pilot);
builder.Services.AddSingleton<PilotRuntime>();

string[] origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("runs", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 40, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseCors();
app.UseRateLimiter();

var api = app.MapGroup("/api");

api.MapGet("/health", (PilotRuntime runtime) => new
{
    status = "ok",
    mode = pilot.IsLive ? "openai" : "demo",
    model = pilot.ModelLabel,
    crm = pilot.CrmMode.Equals("http", StringComparison.OrdinalIgnoreCase) ? $"MCP over HTTP ({pilot.CrmEndpoint})" : "MCP in-process",
    workspaces = runtime.Count,
});

api.MapGet("/agents", () => AgentRoster.Describe());

api.MapGet("/graph", () => LeadWorkflowFactory.Describe());

api.MapPost("/workspaces", async (PilotRuntime runtime, CancellationToken ct) =>
{
    PilotSession session = await runtime.CreateAsync(ct);
    return TypedResults.Created($"/api/workspaces/{session.Id}", await session.SnapshotAsync(ct));
}).RequireRateLimiting("runs");

api.MapGet("/workspaces/{id}", async Task<IResult> (string id, PilotRuntime runtime, CancellationToken ct) =>
    runtime.Get(id) is { } session ? TypedResults.Ok(await session.SnapshotAsync(ct)) : TypedResults.NotFound());

api.MapPost("/workspaces/{id}/leads", async Task<IResult> (string id, NewLead body, PilotRuntime runtime, CancellationToken ct) =>
{
    if (runtime.Get(id) is not { } session) return TypedResults.NotFound();
    if (Validate(body) is { } problem) return TypedResults.BadRequest(new { error = problem });
    return TypedResults.Ok(await session.AddLeadAsync(body, ct));
}).RequireRateLimiting("runs");

api.MapPost("/workspaces/{id}/runs", IResult (string id, RunRequest body, PilotRuntime runtime, CancellationToken ct) =>
{
    if (runtime.Get(id) is not { } session) return TypedResults.NotFound();
    if (string.IsNullOrWhiteSpace(body.LeadId)) return TypedResults.BadRequest(new { error = "leadId is required." });
    if (session.IsBusy) return TypedResults.Conflict(new { error = "This workspace is already running a lead." });

    return TypedResults.ServerSentEvents(ToSse(session.RunAsync(body.LeadId.Trim(), ct), ct));
}).RequireRateLimiting("runs");

api.MapPost("/workspaces/{id}/runs/{runId}/decision", IResult (string id, string runId, ApprovalDecision body, PilotRuntime runtime, CancellationToken ct) =>
{
    if (runtime.Get(id) is not { } session) return TypedResults.NotFound();
    if (session.IsBusy) return TypedResults.Conflict(new { error = "This workspace is busy." });
    if (session.Pending?.RunId != runId) return TypedResults.Conflict(new { error = "No run is waiting for approval with that id." });
    if (body.Subject?.Length > 200 || body.Body?.Length > 4000 || body.Note?.Length > 500)
    {
        return TypedResults.BadRequest(new { error = "Edited email or note is too long." });
    }

    return TypedResults.ServerSentEvents(ToSse(session.DecideAsync(runId, body, ct), ct));
}).RequireRateLimiting("runs");

app.Run();

static string? Validate(NewLead lead)
{
    if (string.IsNullOrWhiteSpace(lead.Name) || lead.Name.Length > 80) return "Name is required (max 80 characters).";
    if (string.IsNullOrWhiteSpace(lead.Title) || lead.Title.Length > 80) return "Title is required (max 80 characters).";
    if (!MailAddress.TryCreate(lead.Email, out _) || lead.Email.Length > 120) return "A valid email is required.";
    if (lead.Company?.Length > 80) return "Company is too long.";
    if (string.IsNullOrWhiteSpace(lead.Country) || lead.Country.Trim().Length != 2) return "Country must be a 2-letter ISO code.";
    if (string.IsNullOrWhiteSpace(lead.Source) || lead.Source.Length > 80) return "Source is required (max 80 characters).";
    if (lead.Message?.Length > 600) return "Message is too long (max 600 characters).";
    return null;
}

static async IAsyncEnumerable<SseItem<object>> ToSse(IAsyncEnumerable<StreamEvent> events, [EnumeratorCancellation] CancellationToken ct)
{
    await using IAsyncEnumerator<StreamEvent> e = events.GetAsyncEnumerator(ct);
    while (true)
    {
        StreamEvent? current = null;
        string? failure = null;
        try
        {
            if (!await e.MoveNextAsync()) break;
            current = e.Current;
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        if (failure is not null)
        {
            yield return new SseItem<object>(new ErrorData(failure), "error");
            yield break;
        }

        yield return new SseItem<object>(current!.Data, current.Type);
    }
}

public sealed record RunRequest(string? LeadId);

public partial class Program;
