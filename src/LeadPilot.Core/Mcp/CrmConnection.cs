using System.IO.Pipelines;
using System.Reflection;
using System.Text.Json;
using LeadPilot.Crm;
using LeadPilot.Crm.Domain;
using LeadPilot.Crm.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LeadPilot.Core.Mcp;

public sealed class CrmToolException(string message) : Exception(message);

/// <summary>
/// An MCP client session against the Fernway CRM server. In "embedded" mode the server runs in-process over
/// in-memory pipes (still the real MCP protocol); in "http" mode it connects to the standalone CRM server.
/// Either way each connection is its own CRM sandbox.
/// </summary>
public sealed class CrmConnection : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly Dictionary<string, McpClientTool> _tools;
    private readonly IAsyncDisposable? _embeddedServer;

    private CrmConnection(McpClient client, IList<McpClientTool> tools, string transport, IAsyncDisposable? embeddedServer)
    {
        _client = client;
        _tools = tools.ToDictionary(t => t.Name);
        _embeddedServer = embeddedServer;
        Transport = transport;
    }

    public string Transport { get; }

    public IReadOnlyCollection<McpClientTool> Tools => _tools.Values;

    public static async Task<CrmConnection> ConnectAsync(string mode, string? endpoint, HttpClient? httpClient = null, CancellationToken ct = default)
    {
        if (mode.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint ?? "http://localhost:5090/mcp"),
                Name = "fernway-crm",
            };
            HttpClientTransport transport = httpClient is null
                ? new HttpClientTransport(transportOptions)
                : new HttpClientTransport(transportOptions, httpClient, ownsHttpClient: false);
            McpClient http = await McpClient.CreateAsync(transport, cancellationToken: ct);
            return new CrmConnection(http, await http.ListToolsAsync(cancellationToken: ct), $"http {endpoint}", null);
        }

        Pipe clientToServer = new(), serverToClient = new();
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "fernway-crm", Version = "1.0.0" },
            ToolCollection = [.. CreateTools(new CrmTools(new CrmWorkspaces()))],
        };
        McpServer server = McpServer.Create(new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()), options);
        Task serverRun = server.RunAsync(CancellationToken.None);

        McpClient client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()), cancellationToken: ct);
        return new CrmConnection(client, await client.ListToolsAsync(cancellationToken: ct), "embedded (in-memory MCP)", new EmbeddedServer(server, serverRun, clientToServer, serverToClient));
    }

    public McpClientTool Tool(string name) =>
        _tools.TryGetValue(name, out McpClientTool? tool) ? tool : throw new InvalidOperationException($"CRM server has no tool '{name}'.");

    public IList<Microsoft.Extensions.AI.AITool> ToolsNamed(IEnumerable<string> names) => [.. names.Select(Tool)];

    /// <summary>Calls a tool directly from code (no model involved) and deserializes its result.</summary>
    public async Task<(T Value, JsonElement Raw)> CallAsync<T>(string tool, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        CallToolResult result = await Tool(tool).CallAsync(args, cancellationToken: ct);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        if (result.IsError == true) throw new CrmToolException(text.Length > 0 ? CleanError(text) : $"{tool} failed.");

        JsonElement raw = result.StructuredContent is { } structured
            ? JsonSerializer.SerializeToElement(structured, CrmJson.Options)
            : JsonDocument.Parse(text.Length > 0 ? text : "null").RootElement.Clone();
        return (raw.Deserialize<T>(CrmJson.Options)!, raw);
    }

    /// <summary>Strips the SDK's "An error occurred invoking 'tool': " prefix from tool error text.</summary>
    public static string CleanError(string text)
    {
        const string prefix = "An error occurred invoking '";
        int end = text.StartsWith(prefix, StringComparison.Ordinal) ? text.IndexOf("': ", prefix.Length, StringComparison.Ordinal) : -1;
        return end < 0 ? text : text[(end + 3)..];
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        if (_embeddedServer is not null) await _embeddedServer.DisposeAsync();
    }

    private static IEnumerable<McpServerTool> CreateTools(CrmTools target) =>
        typeof(CrmTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => McpServerTool.Create(m, target, new McpServerToolCreateOptions { SerializerOptions = CrmJson.Options }));

    private sealed class EmbeddedServer(McpServer server, Task run, Pipe a, Pipe b) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await a.Writer.CompleteAsync();
            await b.Writer.CompleteAsync();
            await server.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
    }
}
