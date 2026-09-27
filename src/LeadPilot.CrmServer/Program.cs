using LeadPilot.Crm;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CrmWorkspaces>();
builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "fernway-crm", Version = "1.0.0" })
    .WithHttpTransport(o => o.SessionMode = ModelContextProtocol.AspNetCore.HttpServerSessionMode.Stateful)
    .WithTools<CrmTools>(CrmJson.Options);

var app = builder.Build();

app.MapMcp("/mcp");
app.MapGet("/", () => Results.Text("Fernway CRM MCP server. Point an MCP client at /mcp (Streamable HTTP)."));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
