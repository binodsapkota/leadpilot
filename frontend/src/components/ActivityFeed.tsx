import { useEffect, useRef, useState } from "react";
import { NODE_META } from "../nodes";
import type { Activity, RunState } from "../run";

export function ActivityFeed({ run }: { run?: RunState }) {
  const list = useRef<HTMLOListElement>(null);
  const count = run?.activity.length ?? 0;

  useEffect(() => {
    const el = list.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [count]);

  const tools = run?.activity.filter((a) => a.kind === "tool").length ?? 0;

  return (
    <section className="card activity-card">
      <div className="card-head">
        <h2>Activity</h2>
        {run && (
          <span className="muted small">
            {tools} MCP tool call{tools === 1 ? "" : "s"}
          </span>
        )}
      </div>
      {!run ? (
        <p className="muted empty">Pick a lead and press Run. Every agent step and MCP tool call shows up here as it happens.</p>
      ) : (
        <ol className="activity" ref={list}>
          {run.activity.map((a) => (
            <Row key={a.id} a={a} />
          ))}
        </ol>
      )}
    </section>
  );
}

function Row({ a }: { a: Activity }) {
  const [open, setOpen] = useState(false);
  const time = <span className="act-time mono">{(a.at / 1000).toFixed(1)}s</span>;

  switch (a.kind) {
    case "node": {
      const meta = NODE_META[a.node];
      return (
        <li className={`act act-node ${a.phase}`} style={{ "--agent": meta.color } as React.CSSProperties}>
          {time}
          <span className="act-dot" />
          <span className="act-who">{meta.label}</span>
          <span className="act-text">
            {a.phase === "start" ? (a.attempt > 1 ? `started revision ${a.attempt}` : "started") : a.summary}
          </span>
          {a.phase === "done" && a.ms !== undefined && <span className="act-ms mono">{a.ms} ms</span>}
        </li>
      );
    }
    case "tool": {
      const meta = NODE_META[a.node];
      const state = a.call.ok === undefined ? "running" : a.call.ok ? "ok" : "fail";
      return (
        <li className="act act-tool" style={{ "--agent": meta.color } as React.CSSProperties}>
          <button className="act-line" onClick={() => setOpen(!open)}>
            {time}
            <span className="act-indent" />
            <span className={`tool-tag ${a.call.caller}`}>{a.call.caller === "agent" ? "agent" : "code"}</span>
            <code className="act-tool-name">{a.call.tool}</code>
            <span className="act-args mono">{preview(a.call.args)}</span>
            <span className={`act-state ${state}`}>{state === "running" ? "…" : state === "ok" ? `${a.call.ms} ms` : "error"}</span>
          </button>
          {open && (
            <div className="act-json">
              <div>
                <span>arguments</span>
                <pre>{JSON.stringify(a.call.args, null, 2)}</pre>
              </div>
              {a.call.result !== undefined && (
                <div>
                  <span>result</span>
                  <pre>{JSON.stringify(a.call.result, null, 2)}</pre>
                </div>
              )}
            </div>
          )}
        </li>
      );
    }
    case "write":
      return (
        <li className={`act act-write ${a.ok ? "ok" : "fail"}`}>
          {time}
          <span className="act-dot" />
          <code className="act-tool-name">{a.tool}</code>
          <span className="act-text">{a.ok ? a.description : `Refused: ${a.message}`}</span>
        </li>
      );
    case "approval":
      return (
        <li className="act act-approval">
          {time}
          <span className="act-dot" />
          <span className="act-text">{a.text}</span>
        </li>
      );
    case "error":
      return (
        <li className="act act-error">
          {time}
          <span className="act-dot" />
          <span className="act-text">{a.message}</span>
        </li>
      );
  }
}

function preview(args: unknown) {
  if (!args || typeof args !== "object") return "";
  return Object.entries(args as Record<string, unknown>)
    .filter(([k]) => k !== "body")
    .map(([k, v]) => `${k}=${typeof v === "string" ? (v.length > 24 ? v.slice(0, 22) + "…" : v) : JSON.stringify(v)}`)
    .join(" ");
}
