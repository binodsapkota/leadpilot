namespace LeadPilot.Crm.Domain;

/// <summary>One CRM workspace. Reference data (companies, news, reps) lives in <see cref="SeedData"/>.</summary>
public sealed class CrmData
{
    private int _seq;

    public object Gate { get; } = new();
    public List<InboundLead> Leads { get; } = [];
    public List<Account> Accounts { get; } = [];
    public List<Contact> Contacts { get; } = [];
    public List<Opportunity> Opportunities { get; } = [];
    public List<Activity> Activities { get; } = [];
    public List<CrmTask> Tasks { get; } = [];
    public List<OutboxEmail> Outbox { get; } = [];
    public List<CrmChange> Changes { get; } = [];
    public List<DoNotContactEntry> DoNotContact { get; } = [];

    public int NextId() => Interlocked.Increment(ref _seq);

    public void Record(string type, string target, string summary) =>
        Changes.Add(new CrmChange(Changes.Count + 1, type, target, summary));

    public InboundLead? Lead(string id) => Leads.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static CompanyProfile? Company(string domain) =>
        SeedData.Companies.FirstOrDefault(c => c.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

    public static Rep? Rep(string id) => SeedData.Reps.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Isolates CRM state per MCP session so concurrent visitors (or test runs) never see each other's changes.
/// Transports without sessions (stdio, in-memory) share a single workspace.
/// </summary>
public sealed class CrmWorkspaces
{
    private const int MaxWorkspaces = 500;
    private readonly Dictionary<string, (CrmData Data, long Touched)> _bySession = [];
    private readonly CrmData _default = SeedData.Create();

    public CrmData Get(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return _default;

        lock (_bySession)
        {
            if (!_bySession.TryGetValue(sessionId, out var entry))
            {
                if (_bySession.Count >= MaxWorkspaces)
                {
                    _bySession.Remove(_bySession.MinBy(kv => kv.Value.Touched).Key);
                }
                entry = (SeedData.Create(), 0);
            }
            _bySession[sessionId] = (entry.Data, Environment.TickCount64);
            return entry.Data;
        }
    }
}
