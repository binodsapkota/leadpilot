import type { NodeId } from "./types";

export const NODE_META: Record<NodeId, { label: string; color: string }> = {
  intake: { label: "Intake", color: "#94a3b8" },
  research: { label: "Researcher", color: "#38bdf8" },
  history: { label: "CRM Historian", color: "#a78bfa" },
  fit: { label: "Fit Scorer", color: "#f472b6" },
  compliance: { label: "Compliance", color: "#94a3b8" },
  strategist: { label: "Strategist", color: "#fbbf24" },
  copywriter: { label: "Copywriter", color: "#34d399" },
  reviewer: { label: "Reviewer", color: "#fb923c" },
  approval: { label: "Rep approval", color: "#fbbf24" },
  crm_update: { label: "CRM update", color: "#94a3b8" },
};

export const STATUS_CLASS: Record<string, string> = {
  New: "st-new",
  Working: "st-working",
  Nurture: "st-nurture",
  Disqualified: "st-dq",
  "Do not contact": "st-dnc",
  "Needs review": "st-review",
};

export const DECISION_LABEL: Record<string, string> = {
  pursue: "Pursue",
  nurture: "Nurture",
  disqualify: "Disqualify",
  blocked: "Blocked",
};
