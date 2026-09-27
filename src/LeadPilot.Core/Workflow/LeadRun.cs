using LeadPilot.Core.Agents;
using LeadPilot.Core.Mcp;
using LeadPilot.Crm.Tools;
using Microsoft.Agents.AI;

namespace LeadPilot.Core.Workflow;

/// <summary>
/// Per-run state shared by the executors of one workflow instance. A new workflow is built for every lead run,
/// so nothing here is shared across runs.
/// </summary>
public sealed class LeadRun(CrmConnection crm, IReadOnlyDictionary<string, AIAgent> agents, int maxDrafts)
{
    public CrmConnection Crm { get; } = crm;
    public IReadOnlyDictionary<string, AIAgent> Agents { get; } = agents;
    public int MaxDrafts { get; } = maxDrafts;

    public LeadSummary? Lead { get; set; }
    public LeadBrief? Brief { get; set; }
    public List<EmailDraft> Drafts { get; } = [];
    public List<ReviewResult> Reviews { get; } = [];
    public ApprovalRequest? Approval { get; set; }
    public List<PlannedWrite> PlannedWrites { get; set; } = [];

    public AIAgent Agent(string key) => Agents[key];

    public static string AgentFor(string node) => node switch
    {
        Nodes.Research => AgentRoster.Researcher,
        Nodes.History => AgentRoster.Historian,
        Nodes.Fit => AgentRoster.Qualifier,
        Nodes.Strategist => AgentRoster.Strategist,
        Nodes.Copywriter => AgentRoster.Copywriter,
        Nodes.Reviewer => AgentRoster.Reviewer,
        _ => node,
    };
}
