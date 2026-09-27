import type { CrmChange } from "../types";

const LABEL: Record<string, string> = {
  lead_created: "Lead created",
  lead_updated: "Lead updated",
  activity_logged: "Activity logged",
  task_created: "Task created",
  email_queued: "Email queued",
};

export function CrmPanel({ changes, transport }: { changes: CrmChange[]; transport: string }) {
  return (
    <div className="crm">
      <p className="muted small">
        Everything LeadPilot wrote to this sandboxed CRM, read back through the <code>get_crm_changes</code> MCP tool ({transport}).
        Emails are only queued, never sent.
      </p>
      {changes.length === 0 ? (
        <p className="muted empty">No changes yet. Nothing is written until a rep approves.</p>
      ) : (
        <ol className="changes">
          {[...changes].reverse().map((c) => (
            <li key={c.seq} className={`change ch-${c.type}`}>
              <div>
                <span className="change-type">{LABEL[c.type] ?? c.type}</span>
                <span className="mono muted small">
                  #{c.seq} · {c.target}
                </span>
              </div>
              <span className="small">{c.summary}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
