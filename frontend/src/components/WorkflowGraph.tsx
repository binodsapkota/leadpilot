import type { NodeStatus, RunState } from "../run";
import type { NodeId } from "../types";

interface NodeDef {
  id: NodeId;
  label: string;
  sub: string;
  kind: "agent" | "code" | "human";
  x: number;
  y: number;
}

const W = 136;
const H = 50;

const NODES: NodeDef[] = [
  { id: "intake", label: "Intake", sub: "get_lead", kind: "code", x: 72, y: 160 },
  { id: "research", label: "Researcher", sub: "enrich · news", kind: "agent", x: 224, y: 44 },
  { id: "history", label: "CRM Historian", sub: "account history", kind: "agent", x: 224, y: 120 },
  { id: "fit", label: "Fit Scorer", sub: "ICP rubric", kind: "agent", x: 224, y: 196 },
  { id: "compliance", label: "Compliance", sub: "contact policy", kind: "code", x: 224, y: 272 },
  { id: "strategist", label: "Strategist", sub: "decide the play", kind: "agent", x: 376, y: 160 },
  { id: "copywriter", label: "Copywriter", sub: "draft email", kind: "agent", x: 528, y: 62 },
  { id: "reviewer", label: "Reviewer", sub: "policy check", kind: "agent", x: 716, y: 62 },
  { id: "approval", label: "Rep approval", sub: "request port", kind: "human", x: 556, y: 262 },
  { id: "crm_update", label: "CRM update", sub: "MCP writes", kind: "code", x: 716, y: 262 },
];

const byId = Object.fromEntries(NODES.map((n) => [n.id, n])) as Record<NodeId, NodeDef>;

type EdgeDef = { from: NodeId; to: NodeId; label?: string; path?: string };

const EDGES: EdgeDef[] = [
  { from: "intake", to: "research" },
  { from: "intake", to: "history" },
  { from: "intake", to: "fit" },
  { from: "intake", to: "compliance" },
  { from: "research", to: "strategist" },
  { from: "history", to: "strategist" },
  { from: "fit", to: "strategist" },
  { from: "compliance", to: "strategist" },
  { from: "strategist", to: "copywriter", label: "pursue" },
  { from: "strategist", to: "approval", label: "no email" },
  { from: "copywriter", to: "reviewer", label: "draft", path: "M596,50 C620,28 624,28 646,50" },
  { from: "reviewer", to: "copywriter", label: "revise", path: "M648,74 C624,96 620,96 598,74" },
  { from: "reviewer", to: "approval", label: "approved" },
  { from: "approval", to: "crm_update" },
];

function edgePath(e: EdgeDef) {
  if (e.path) return e.path;
  const a = byId[e.from];
  const b = byId[e.to];
  const x1 = a.x + W / 2;
  const y1 = a.y;
  const x2 = b.x - W / 2;
  const y2 = b.y;
  if (e.from === "reviewer" && e.to === "approval") {
    return `M${a.x - 10},${a.y + H / 2} C${a.x - 10},${a.y + 110} ${b.x + 30},${b.y - 90} ${b.x + 20},${b.y - H / 2}`;
  }
  if (e.from === "strategist" && e.to === "approval") {
    return `M${a.x + 10},${a.y + H / 2} C${a.x + 20},${b.y} ${x2 - 50},${b.y} ${x2},${y2}`;
  }
  const mx = (x1 + x2) / 2;
  return `M${x1},${y1} C${mx},${y1} ${mx},${y2} ${x2},${y2}`;
}

function edgeLit(e: EdgeDef, nodes: RunState["nodes"]) {
  const from = nodes[e.from];
  const to = nodes[e.to];
  if (!from || !to || from.state === "idle") return false;
  if (e.from === "reviewer" && e.to === "copywriter") return (nodes.copywriter?.attempts ?? 0) > 1;
  if (e.from === "strategist" && e.to === "approval") return !nodes.copywriter;
  if (e.from === "reviewer" && e.to === "approval") return true;
  return from.state === "done";
}

function labelPoint(e: EdgeDef): [number, number] {
  if (e.label === "draft") return [622, 24];
  if (e.label === "revise") return [622, 110];
  if (e.label === "approved") return [690, 176];
  if (e.label === "no email") return [440, 276];
  if (e.label === "pursue") return [452, 92];
  return [0, 0];
}

export function WorkflowGraph({ run }: { run?: RunState }) {
  const nodes = run?.nodes ?? {};
  return (
    <svg className="graph" viewBox="0 0 790 304" role="img" aria-label="Lead qualification workflow graph">
      <defs>
        <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="#334155" />
        </marker>
        <marker id="arrow-lit" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="#38bdf8" />
        </marker>
      </defs>

      <text className="lane" x="224" y="11">parallel fan-out</text>
      <text className="lane" x="376" y="124">fan-in barrier</text>
      <text className="lane" x="622" y="128">revision loop</text>

      {EDGES.map((e) => {
        const lit = edgeLit(e, nodes);
        return (
          <g key={`${e.from}-${e.to}`}>
            <path className={`gedge ${lit ? "lit" : ""}`} d={edgePath(e)} markerEnd={`url(#${lit ? "arrow-lit" : "arrow"})`} />
            {e.label && (
              <text className={`gedge-label ${lit ? "lit" : ""}`} x={labelPoint(e)[0]} y={labelPoint(e)[1]}>
                {e.label}
              </text>
            )}
          </g>
        );
      })}

      {NODES.map((n) => (
        <GraphNode key={n.id} def={n} status={nodes[n.id]} />
      ))}
    </svg>
  );
}

function GraphNode({ def, status }: { def: NodeDef; status?: NodeStatus }) {
  const state = status?.state ?? "idle";
  const showAttempts = (def.id === "copywriter" || def.id === "reviewer") && (status?.attempts ?? 0) > 1;
  return (
    <g className={`gnode ${def.kind} ${state}`} transform={`translate(${def.x - W / 2},${def.y - H / 2})`}>
      <rect className="box" width={W} height={H} rx="11" />
      <text className="gnode-label" x="11" y="21">
        {def.label}
      </text>
      <text className="gnode-sub" x="11" y="38">
        {state === "done" && status?.ms !== undefined ? `${(status.ms / 1000).toFixed(1)}s · ${def.sub}` : def.sub}
      </text>
      <circle className="gnode-dot" cx={W - 12} cy="13" r="4.5" />
      {showAttempts && (
        <g transform={`translate(${W - 30},${H - 12})`}>
          <rect className="gnode-badge" width="26" height="16" rx="8" />
          <text className="gnode-badge-text" x="13" y="11.5">
            v{status!.attempts}
          </text>
        </g>
      )}
    </g>
  );
}
