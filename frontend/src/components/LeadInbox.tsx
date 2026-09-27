import { useState } from "react";
import { STATUS_CLASS } from "../nodes";
import type { LeadSummary, NewLead } from "../types";

interface Props {
  leads: LeadSummary[];
  selected?: string;
  runningLead?: string;
  busy: boolean;
  onSelect: (id: string) => void;
  onRun: (id: string) => void;
  onAdd: (lead: NewLead) => Promise<void>;
}

const HINTS: Record<string, string> = {
  "L-201": "Demo request, net-new, reviewer loop",
  "L-202": "Existing customer asking to expand",
  "L-203": "EU lead without GDPR consent",
  "L-204": "Student with a personal email",
  "L-205": "On the do-not-contact list",
  "L-206": "Lost deal that came back",
};

export function LeadInbox({ leads, selected, runningLead, busy, onSelect, onRun, onAdd }: Props) {
  const [adding, setAdding] = useState(false);

  return (
    <section className="card inbox">
      <div className="card-head">
        <h2>Inbound leads</h2>
        <button className="chip" onClick={() => setAdding(!adding)} disabled={busy}>
          {adding ? "Cancel" : "+ Add lead"}
        </button>
      </div>

      {adding && (
        <AddLeadForm
          onSubmit={async (lead) => {
            await onAdd(lead);
            setAdding(false);
          }}
        />
      )}

      <ul className="leads">
        {leads.map((l) => (
          <li key={l.id}>
            <button
              className={`lead ${selected === l.id ? "selected" : ""} ${runningLead === l.id ? "running" : ""}`}
              onClick={() => onSelect(l.id)}
            >
              <div className="lead-top">
                <strong>{l.name}</strong>
                <span className={`lead-status ${STATUS_CLASS[l.status] ?? ""}`}>{l.status}</span>
              </div>
              <span className="lead-sub">
                {l.title}
                {l.company ? ` · ${l.company}` : ""}
              </span>
              <span className="lead-meta">
                <span className="mono">{l.id}</span>
                <span>{l.country}</span>
                <span className="lead-source">{l.source}</span>
                {l.tier && (
                  <span className={`tier tier-${l.tier}`}>
                    {l.score} · {l.tier}
                  </span>
                )}
              </span>
              {HINTS[l.id] && <span className="lead-hint">{HINTS[l.id]}</span>}
            </button>
            {selected === l.id && (
              <button className="run-btn" onClick={() => onRun(l.id)} disabled={busy}>
                {runningLead === l.id && busy ? "Running…" : "Run LeadPilot"}
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

function AddLeadForm({ onSubmit }: { onSubmit: (lead: NewLead) => Promise<void> }) {
  const [lead, setLead] = useState<NewLead>({
    name: "",
    title: "",
    email: "",
    company: "",
    country: "US",
    source: "Contact form",
    message: "",
    marketingConsent: true,
  });
  const [error, setError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const set = (k: keyof NewLead) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setLead({ ...lead, [k]: k === "marketingConsent" ? (e.target as HTMLInputElement).checked : e.target.value });

  return (
    <form
      className="add-lead"
      onSubmit={async (e) => {
        e.preventDefault();
        setSaving(true);
        setError(undefined);
        try {
          await onSubmit(lead);
        } catch (err) {
          setError((err as Error).message);
        } finally {
          setSaving(false);
        }
      }}
    >
      <input placeholder="Full name" value={lead.name} onChange={set("name")} required />
      <input placeholder="Job title" value={lead.title} onChange={set("title")} required />
      <input placeholder="Work email" type="email" value={lead.email} onChange={set("email")} required />
      <div className="row">
        <input placeholder="Company" value={lead.company} onChange={set("company")} />
        <input className="country" placeholder="US" maxLength={2} value={lead.country} onChange={set("country")} required />
      </div>
      <input placeholder="Source (e.g. Demo request form)" value={lead.source} onChange={set("source")} required />
      <textarea placeholder="Message (optional)" rows={2} value={lead.message} onChange={set("message")} />
      <label className="consent">
        <input type="checkbox" checked={lead.marketingConsent} onChange={set("marketingConsent")} /> Marketing consent
      </label>
      {error && <p className="form-error">{error}</p>}
      <button type="submit" className="run-btn" disabled={saving}>
        {saving ? "Adding…" : "Add to inbox (via MCP)"}
      </button>
    </form>
  );
}
