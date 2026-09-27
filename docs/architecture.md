# Architecture: one lead, start to finish

This walks through a single run for Harborline Logistics (lead `L-201`). It covers both the happy path and the
reviewer loop, and shows how framework events become the events the UI draws.

## The pieces

| Piece | Where | Role |
|---|---|---|
| CRM domain and policies | `src/LeadPilot.Crm` | Seed data, ICP scoring, contact policy, email policy. Plain C#, no AI |
| MCP tools | `src/LeadPilot.Crm/Tools/CrmTools.cs` | 18 tools. Each MCP session maps to its own `CrmData` sandbox |
| MCP server | `src/LeadPilot.CrmServer` | Hosts the tools over Streamable HTTP in stateful session mode |
| MCP client | `src/LeadPilot.Core/Mcp/CrmConnection.cs` | One client per workspace. Embedded mode runs the server in-process over in-memory pipes, and HTTP mode connects to the standalone server |
| Agents | `src/LeadPilot.Core/Agents` | Six `ChatClientAgent`s. Each gets only the MCP tools its role needs |
| Workflow | `src/LeadPilot.Core/Workflow` | The graph, the executors, and the code guardrails |
| Session runtime | `src/LeadPilot.Core/Runtime/PilotSession.cs` | Owns one workspace, runs the workflow, parks it at the approval port, maps events |
| API | `src/LeadPilot.Api` | Minimal API that streams `PilotSession` events as server-sent events |

## Workspace creation

`POST /api/workspaces` creates a `PilotSession`:

1. `CrmConnection.ConnectAsync` opens an MCP session and lists the server's tools.
   - In HTTP mode, the server's stateful session id selects a fresh copy of the seed data.
   - In embedded mode, each in-process server has its own copy.
2. `AgentFactory.Create` builds the six agents. Their `Tools` are the `McpClientTool`s for their role, so when a model
   calls `score_lead`, the call goes over MCP.
3. In demo mode, each agent's `IChatClient` is a `DemoChatClient`. In live mode, all six share one OpenAI-compatible
   client.

## A run, superstep by superstep

`POST /api/workspaces/{id}/runs` with `{ "leadId": "L-201" }` calls
`InProcessExecution.Default.RunStreamingAsync(workflow, new LeadRequest("L-201"), runId)`. Agent Framework executes
the graph in supersteps. Messages sent in one superstep are delivered in the next.

1. **Intake.** Calls `get_lead` from code and sends a `LeadContext` over the fan-out edge.
2. **Four branches in parallel.** Research, History, Fit and Compliance all receive the `LeadContext` in the same
   superstep.
   - Each agent branch runs its agent with a JSON schema response format, then applies its guardrail:
     - **Fit** replaces the score with the `score_lead` result.
     - **History** forces net-new when `find_account` found nothing.
   - Compliance calls `check_contact_policy` from code.
   - Each branch sends a typed report: `ResearchReport`, `HistoryReport`, `FitReport` or `PolicyReport`.
3. **Strategist.** The fan-in barrier edge holds the reports until all four arrive, then delivers them together.
   - The strategist agent proposes a `LeadPlan`, and `GovernAsync` applies the rules:
     - blocked stays blocked;
     - no consent means no pursue;
     - tier D net-new is disqualified;
     - ownership follows the relationship or the territory.
   - Any change is recorded in `Overrides`.
   - A pursue plan sends a `DraftRequest`. Anything else sends an `ApprovalRequest` built by `CrmPlanner`.
4. **Copywriter** (draft v1). Streams the email token by token and sends `DraftForReview`.
5. **Reviewer.** Runs its agent, then treats `check_email_policy` as authoritative.
   - v1 cites "70%", which appears in no research fact or proof point, so the review fails.
   - Because the draft count is still under `MaxDrafts`, the reviewer sends a `DraftRequest` carrying the previous
     draft and the issues. That goes back along the loop edge.
6. **Copywriter** (v2), then **Reviewer**. The review passes, and `CrmPlanner.Plan` builds the exact list of CRM
   writes plus an `ApprovalRequest`.
7. **Approval port.** The `ApprovalRequest` reaches `RequestPort<ApprovalRequest, ApprovalDecision>`. The workflow
   emits a `RequestInfoEvent` and goes idle. `WatchStreamAsync(blockOnPendingRequest: false)` then completes, the SSE
   response ends, and the session keeps the paused `StreamingRun`.

Later, `POST .../runs/{runId}/decision` resumes the run:

8. `SendResponseAsync(pending.CreateResponse(decision))` delivers the `ApprovalDecision` to **CRM update**.
9. **CRM update.** Executes the planned writes over MCP, applying the rep's edits to `queue_email`. It emits one
   `crm_write` per call and yields a `RunSummary` as the workflow output.
   - If the CRM refuses a write, for example an edited email that fails policy, the run still completes and reports
     the refusal.
   - On rejection, the only writes are a "Needs review" status and a log entry.

## Event mapping

| Source | Stream event | UI |
|---|---|---|
| `Steps.StartAsync` / `CompleteAsync` (custom `PilotEvent`) | `node_started`, `node_completed` | Graph node turns active, then done, with the draft version badge |
| Agent function call/result in the streaming response | `tool_call`, `tool_result` (caller `agent`) | Activity feed |
| `Steps.CallCrmAsync` (code calling MCP) | `tool_call`, `tool_result` (caller `code`) | Activity feed, tagged "code" |
| Copywriter text updates | `token` | Draft types out live |
| Typed reports | `report` | Brief panel (research, history, fit, policy, plan), email versions, review issues |
| `RequestInfoEvent` | `node_started` (approval), `approval_required` | Approval card with editable email and planned writes |
| CRM writer | `crm_write` | CRM changes panel |
| `WorkflowOutputEvent` carrying `RunSummary` | `run_completed` | Outcome banner, lead status refresh |
| `ExecutorFailedEvent`, `WorkflowErrorEvent` | `error` | Error banner |

## How the demo agents work

`DemoChatClient` implements `IChatClient`. It finds the task context in the prompt and the tool results in the chat
history, then hands both to a per-agent "brain":

1. **First turn.** The brain returns `FunctionCallContent` for the tools it needs.
2. **The framework takes over.** `FunctionInvokingChatClient`, the same code path as with a real model, invokes those
   tools over MCP and calls the client again.
3. **Second turn.** The brain composes its answer from the real tool results. The copywriter's answer is streamed in
   small chunks to mimic token output.

Small per-agent delays make the parallel fan-out visible in the UI. `LeadPilot__DemoPace` scales them, and the tests
set it to 0.

## Concurrency and lifetime

- A workspace runs one lead at a time. A second run gets `409`.
- Starting a new run abandons a run that is paused for approval.
- Workspaces idle for 30 minutes are disposed, which also closes their MCP session. There's a cap of 200 workspaces.
- The standalone CRM server keeps a bounded number of session sandboxes.
