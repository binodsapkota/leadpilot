export type NodeId =
  | "intake"
  | "research"
  | "history"
  | "fit"
  | "compliance"
  | "strategist"
  | "copywriter"
  | "reviewer"
  | "approval"
  | "crm_update";

export interface Health {
  status: string;
  mode: "demo" | "openai";
  model: string;
  crm: string;
  workspaces: number;
}

export interface LeadSummary {
  id: string;
  name: string;
  title: string;
  email: string;
  company: string;
  domain: string;
  country: string;
  source: string;
  message: string;
  marketingConsent: boolean;
  receivedOn: string;
  status: string;
  score: number | null;
  tier: string | null;
  ownerId: string | null;
  ownerName: string | null;
}

export interface CrmChange {
  seq: number;
  type: string;
  target: string;
  summary: string;
}

export interface Fact {
  id: string;
  text: string;
  source: string;
}

export interface ResearchReport {
  summary: string;
  facts: Fact[];
  signals: string[];
}

export interface HistoryReport {
  relationship: "net_new" | "existing_customer" | "former_opportunity";
  accountId: string | null;
  ownerId: string | null;
  ownerName: string | null;
  summary: string;
  notes: string[];
}

export interface ScoreLine {
  criterion: string;
  points: number;
  max: number;
  detail: string;
}

export interface FitData {
  score: number;
  tier: string;
  reasons: string[];
  breakdown: ScoreLine[];
}

export interface PolicyReport {
  status: "clear" | "consent_required" | "blocked";
  reasons: string[];
  allowedChannels: string[];
}

export interface LeadPlan {
  decision: "pursue" | "nurture" | "disqualify" | "blocked";
  ownerId: string | null;
  ownerName: string | null;
  priority: string;
  angle: string;
  rationale: string;
  talkingPoints: string[];
  nextSteps: string[];
  overrides: string[];
}

export interface EmailDraft {
  version: number;
  subject: string;
  body: string;
}

export interface ReviewIssue {
  severity: "error" | "warning";
  message: string;
  excerpt: string | null;
}

export interface ReviewResult {
  version: number;
  passed: boolean;
  issues: ReviewIssue[];
  summary: string;
}

export interface ApprovalRequest {
  leadId: string;
  leadName: string;
  company: string;
  plan: LeadPlan;
  fit: { score: number; tier: string; reasons: string[] };
  policy: PolicyReport;
  draft: EmailDraft | null;
  review: ReviewResult | null;
  proposedChanges: string[];
}

export interface PendingApproval {
  runId: string;
  requestId: string;
  request: ApprovalRequest;
}

export interface Workspace {
  id: string;
  mode: string;
  transport: string;
  leads: LeadSummary[];
  changes: CrmChange[];
  pending: PendingApproval | null;
}

export interface RunSummary {
  leadId: string;
  decision: string;
  approved: boolean;
  outcome: string;
  changes: string[];
  errors: string[];
}

export interface GraphNode {
  id: NodeId;
  label: string;
  kind: "agent" | "code" | "human";
  detail: string;
}

export interface NewLead {
  name: string;
  title: string;
  email: string;
  company: string;
  country: string;
  source: string;
  message: string;
  marketingConsent: boolean;
}

export type PilotEvent =
  | { type: "run_started"; data: { runId: string; leadId: string; mode: string; transport: string } }
  | { type: "node_started"; data: { node: NodeId; attempt: number } }
  | { type: "node_completed"; data: { node: NodeId; ms: number; summary: string } }
  | { type: "tool_call"; data: { node: NodeId; tool: string; callId: string; args: unknown; caller: "agent" | "code" } }
  | { type: "tool_result"; data: { node: NodeId; tool: string; callId: string; ok: boolean; result: unknown; ms: number } }
  | { type: "token"; data: { node: NodeId; version: number; text: string } }
  | { type: "report"; data: { node: NodeId; kind: string; data: unknown } }
  | { type: "crm_write"; data: { tool: string; description: string; ok: boolean; message: string } }
  | { type: "approval_required"; data: { requestId: string; request: ApprovalRequest } }
  | { type: "run_completed"; data: { summary: RunSummary } }
  | { type: "error"; data: { message: string } };
