import { useCallback, useEffect, useRef, useState } from "react";
import { addLead, createWorkspace, decide, getHealth, getWorkspace, runLead } from "./api";
import { ActivityFeed } from "./components/ActivityFeed";
import { ApprovalCard } from "./components/ApprovalCard";
import { BriefPanel } from "./components/BriefPanel";
import { CrmPanel } from "./components/CrmPanel";
import { EmailPanel } from "./components/EmailPanel";
import { LeadInbox } from "./components/LeadInbox";
import { WorkflowGraph } from "./components/WorkflowGraph";
import { newRun, reduce, type RunState } from "./run";
import type { Health, NewLead, PilotEvent, Workspace } from "./types";

type Tab = "brief" | "email" | "crm";

export default function App() {
  const [health, setHealth] = useState<Health>();
  const [workspace, setWorkspace] = useState<Workspace>();
  const [selected, setSelected] = useState<string>("L-201");
  const [run, setRun] = useState<RunState>();
  const [busy, setBusy] = useState(false);
  const [tab, setTab] = useState<Tab>("brief");
  const [error, setError] = useState<string>();
  const started = useRef(false);

  const refresh = useCallback(async (id: string) => setWorkspace(await getWorkspace(id)), []);

  const reset = useCallback(async () => {
    setError(undefined);
    setRun(undefined);
    try {
      setWorkspace(await createWorkspace());
    } catch (e) {
      setError(`Could not reach the API: ${(e as Error).message}`);
    }
  }, []);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    getHealth().then(setHealth).catch(() => undefined);
    void reset();
  }, [reset]);

  const onEvent = useCallback((e: PilotEvent) => {
    setRun((r) => (r ? reduce(r, e) : r));
    if (e.type === "node_started" && e.data.node === "copywriter") setTab("email");
    if (e.type === "crm_write") setTab("crm");
  }, []);

  const start = async (leadId: string) => {
    if (!workspace || busy) return;
    setBusy(true);
    setError(undefined);
    setTab("brief");
    setRun(newRun(leadId));
    try {
      await runLead(workspace.id, leadId, onEvent);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
      await refresh(workspace.id);
    }
  };

  const onDecide = async (d: { approved: boolean; subject?: string; body?: string; note?: string }) => {
    if (!workspace || !run?.runId || busy) return;
    setBusy(true);
    setRun((r) => (r ? { ...r, decision: d.approved ? "approved" : "rejected", phase: "writing" } : r));
    try {
      await decide(workspace.id, run.runId, d, onEvent);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
      await refresh(workspace.id);
    }
  };

  const onAdd = async (lead: NewLead) => {
    if (!workspace) return;
    const created = await addLead(workspace.id, lead);
    await refresh(workspace.id);
    setSelected(created.id);
  };

  const phase = run ? PHASE[run.phase] : undefined;

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <img src="/favicon.svg" width="30" height="30" alt="" />
          <div>
            <h1>LeadPilot</h1>
            <p>Multi-agent sales pipeline copilot for Fernway · Microsoft Agent Framework workflow + CRM over MCP</p>
          </div>
        </div>
        <div className="topbar-right">
          {health && <span className={`badge ${health.mode}`}>{health.mode === "demo" ? "Offline demo agents" : health.model}</span>}
          {health && <span className="badge subtle">{health.crm}</span>}
          <button className="ghost" onClick={reset} disabled={busy}>
            Reset CRM
          </button>
        </div>
      </header>

      {error && <div className="banner">{error}</div>}

      <main className="layout">
        <aside className="left">
          <LeadInbox
            leads={workspace?.leads ?? []}
            selected={selected}
            runningLead={run?.leadId}
            busy={busy}
            onSelect={setSelected}
            onRun={start}
            onAdd={onAdd}
          />
        </aside>

        <section className="center">
          <section className="card graph-card">
            <div className="card-head">
              <h2>Workflow</h2>
              {phase && <span className={`phase ${run!.phase}`}>{phase}</span>}
            </div>
            <WorkflowGraph run={run} />
          </section>
          <ActivityFeed run={run} />
        </section>

        <aside className="right">
          {run?.approval && <ApprovalCard key={run.approval.requestId} run={run} busy={busy} onDecide={onDecide} />}
          <section className="card tabs-card">
            <div className="tabs">
              {(["brief", "email", "crm"] as Tab[]).map((t) => (
                <button key={t} className={tab === t ? "on" : ""} onClick={() => setTab(t)}>
                  {t === "brief" ? "Brief" : t === "email" ? "Email" : "CRM changes"}
                  {t === "email" && run && run.drafts.length > 0 && <span className="count">{run.drafts.length}</span>}
                  {t === "crm" && workspace && workspace.changes.length > 0 && <span className="count">{workspace.changes.length}</span>}
                </button>
              ))}
            </div>
            <div className="tab-body">
              {tab === "brief" && <BriefPanel run={run} />}
              {tab === "email" && <EmailPanel run={run} />}
              {tab === "crm" && <CrmPanel changes={workspace?.changes ?? []} transport={workspace?.transport ?? ""} />}
            </div>
          </section>
        </aside>
      </main>
    </div>
  );
}

const PHASE: Record<RunState["phase"], string> = {
  running: "Agents working",
  awaiting: "Waiting for rep",
  writing: "Writing to CRM",
  done: "Done",
  failed: "Failed",
};
