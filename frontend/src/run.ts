import type {
  ApprovalRequest,
  EmailDraft,
  FitData,
  HistoryReport,
  LeadPlan,
  LeadSummary,
  NodeId,
  PilotEvent,
  PolicyReport,
  ResearchReport,
  ReviewResult,
  RunSummary,
} from "./types";

export type NodeState = "idle" | "running" | "done" | "failed" | "waiting";

export interface NodeStatus {
  state: NodeState;
  attempts: number;
  ms?: number;
  summary?: string;
}

export interface ToolCall {
  callId: string;
  tool: string;
  caller: "agent" | "code";
  args: unknown;
  result?: unknown;
  ok?: boolean;
  ms?: number;
}

export type Activity =
  | { id: string; kind: "node"; node: NodeId; phase: "start" | "done"; attempt: number; summary?: string; ms?: number; at: number }
  | { id: string; kind: "tool"; node: NodeId; call: ToolCall; at: number }
  | { id: string; kind: "write"; tool: string; description: string; ok: boolean; message: string; at: number }
  | { id: string; kind: "approval"; text: string; at: number }
  | { id: string; kind: "error"; message: string; at: number };

export interface RunState {
  runId?: string;
  leadId: string;
  phase: "running" | "awaiting" | "writing" | "done" | "failed";
  startedAt: number;
  nodes: Partial<Record<NodeId, NodeStatus>>;
  activity: Activity[];
  lead?: LeadSummary;
  research?: ResearchReport;
  history?: HistoryReport;
  fit?: FitData;
  policy?: PolicyReport;
  plan?: LeadPlan;
  drafts: EmailDraft[];
  streaming: Record<number, string>;
  reviews: ReviewResult[];
  approval?: { requestId: string; request: ApprovalRequest };
  decision?: "approved" | "rejected";
  writes: { tool: string; description: string; ok: boolean; message: string }[];
  summary?: RunSummary;
  error?: string;
}

export const newRun = (leadId: string): RunState => ({
  leadId,
  phase: "running",
  startedAt: performance.now(),
  nodes: {},
  activity: [],
  drafts: [],
  streaming: {},
  reviews: [],
  writes: [],
});

let seq = 0;
const id = () => `a${++seq}`;

export function reduce(run: RunState, e: PilotEvent): RunState {
  const at = performance.now() - run.startedAt;
  const push = (a: Activity) => [...run.activity, a];

  switch (e.type) {
    case "run_started":
      return { ...run, runId: e.data.runId };

    case "node_started": {
      const status: NodeStatus = { state: e.data.node === "approval" ? "waiting" : "running", attempts: e.data.attempt };
      return {
        ...run,
        phase: e.data.node === "approval" ? "awaiting" : e.data.node === "crm_update" ? "writing" : run.phase,
        nodes: { ...run.nodes, [e.data.node]: status },
        activity:
          e.data.node === "approval"
            ? run.activity
            : push({ id: id(), kind: "node", node: e.data.node, phase: "start", attempt: status.attempts, at }),
      };
    }

    case "node_completed": {
      const prev = run.nodes[e.data.node] ?? { state: "idle", attempts: 1 };
      return {
        ...run,
        nodes: { ...run.nodes, [e.data.node]: { ...prev, state: "done", ms: e.data.ms, summary: e.data.summary } },
        activity: push({
          id: id(),
          kind: "node",
          node: e.data.node,
          phase: "done",
          attempt: prev.attempts,
          summary: e.data.summary,
          ms: e.data.ms,
          at,
        }),
      };
    }

    case "tool_call":
      return {
        ...run,
        activity: push({
          id: id(),
          kind: "tool",
          node: e.data.node,
          call: { callId: e.data.callId, tool: e.data.tool, caller: e.data.caller, args: e.data.args },
          at,
        }),
      };

    case "tool_result":
      return {
        ...run,
        activity: run.activity.map((a) =>
          a.kind === "tool" && a.call.callId === e.data.callId
            ? { ...a, call: { ...a.call, result: e.data.result, ok: e.data.ok, ms: e.data.ms } }
            : a,
        ),
      };

    case "token":
      return {
        ...run,
        streaming: { ...run.streaming, [e.data.version]: (run.streaming[e.data.version] ?? "") + e.data.text },
      };

    case "report":
      return applyReport(run, e.data.kind, e.data.data);

    case "approval_required":
      return {
        ...run,
        phase: "awaiting",
        approval: e.data,
        activity: push({ id: id(), kind: "approval", text: "Waiting for rep approval before any CRM write", at }),
      };

    case "crm_write":
      return { ...run, writes: [...run.writes, e.data], activity: push({ id: id(), kind: "write", ...e.data, at }) };

    case "run_completed":
      return { ...run, phase: "done", summary: e.data.summary };

    case "error":
      return {
        ...run,
        phase: "failed",
        error: e.data.message,
        nodes: Object.fromEntries(
          Object.entries(run.nodes).map(([k, v]) => [k, v && v.state === "running" ? { ...v, state: "failed" } : v]),
        ),
        activity: push({ id: id(), kind: "error", message: e.data.message, at }),
      };
  }
}

function applyReport(run: RunState, kind: string, data: unknown): RunState {
  switch (kind) {
    case "lead":
      return { ...run, lead: data as LeadSummary };
    case "research":
      return { ...run, research: data as ResearchReport };
    case "history":
      return { ...run, history: data as HistoryReport };
    case "fit":
      return { ...run, fit: data as FitData };
    case "policy":
      return { ...run, policy: data as PolicyReport };
    case "plan":
      return { ...run, plan: data as LeadPlan };
    case "draft": {
      const draft = data as EmailDraft;
      return { ...run, drafts: [...run.drafts.filter((d) => d.version !== draft.version), draft] };
    }
    case "review": {
      const review = data as ReviewResult;
      return { ...run, reviews: [...run.reviews.filter((r) => r.version !== review.version), review] };
    }
    default:
      return run;
  }
}
