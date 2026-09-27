using System.Text.Json;
using Microsoft.Agents.AI.Workflows;

namespace LeadPilot.Core.Workflow;

/// <summary>A UI-facing event raised from inside an executor via <see cref="IWorkflowContext.AddEventAsync"/>.</summary>
public sealed class PilotEvent(string type, object data) : WorkflowEvent(data)
{
    public string Type { get; } = type;
    public object Payload { get; } = data;
}

public sealed record NodeStartedData(string Node, int Attempt);

public sealed record NodeCompletedData(string Node, long Ms, string Summary);

public sealed record ToolCallData(string Node, string Tool, string CallId, JsonElement Args, string Caller);

public sealed record ToolResultData(string Node, string Tool, string CallId, bool Ok, JsonElement Result, long Ms);

public sealed record TokenData(string Node, int Version, string Text);

public sealed record ReportData(string Node, string Kind, object Data);

public sealed record CrmWriteData(string Tool, string Description, bool Ok, string Message);

public sealed record ApprovalRequiredData(string RequestId, ApprovalRequest Request);

public sealed record RunCompletedData(RunSummary Summary);

public sealed record ErrorData(string Message);
