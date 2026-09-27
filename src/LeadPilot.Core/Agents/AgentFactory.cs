using LeadPilot.Core.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Agents;

public static class AgentFactory
{
    /// <summary>Builds every agent with only the MCP tools its role needs.</summary>
    public static IReadOnlyDictionary<string, AIAgent> Create(CrmConnection crm, Func<AgentSpec, IChatClient> chatClientFor) =>
        AgentRoster.All.ToDictionary(
            spec => spec.Key,
            AIAgent (spec) => new ChatClientAgent(chatClientFor(spec), new ChatClientAgentOptions
            {
                Name = spec.DisplayName,
                Description = spec.Role,
                ChatOptions = new ChatOptions
                {
                    Instructions = spec.Instructions,
                    Tools = crm.ToolsNamed(spec.ToolNames),
                    Temperature = 0.2f,
                },
            }));
}
