import { DECISION_LABEL } from "../nodes";
import type { RunState } from "../run";

const RELATIONSHIP: Record<string, string> = {
  net_new: "Net-new logo",
  existing_customer: "Existing customer",
  former_opportunity: "Former opportunity",
};

const POLICY: Record<string, string> = {
  clear: "Clear to contact",
  consent_required: "Consent required",
  blocked: "Blocked",
};

export function BriefPanel({ run }: { run?: RunState }) {
  if (!run?.lead) return <p className="muted empty">The brief fills in as the parallel agents report back.</p>;
  const { lead, research, history, fit, policy, plan } = run;

  return (
    <div className="brief">
      <div className="brief-lead">
        <strong>{lead.name}</strong>
        <span className="muted small">
          {lead.title}
          {lead.company ? ` · ${lead.company}` : ""} · {lead.country}
        </span>
        {lead.message && <blockquote>“{lead.message}”</blockquote>}
      </div>

      {plan && (
        <div className={`decision d-${plan.decision}`}>
          <div className="decision-top">
            <span className="decision-label">{DECISION_LABEL[plan.decision]}</span>
            {plan.ownerName && <span className="muted small">owner: {plan.ownerName}</span>}
            <span className={`priority p-${plan.priority}`}>{plan.priority}</span>
          </div>
          {plan.angle && <p className="angle">{plan.angle}</p>}
          <p className="small muted">{plan.rationale}</p>
          {plan.overrides.length > 0 && (
            <div className="overrides">
              <span>Guardrail overrides</span>
              <ul>
                {plan.overrides.map((o) => (
                  <li key={o}>{o}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      <div className="brief-grid">
        <div className="brief-box">
          <h3>Fit</h3>
          {fit ? (
            <>
              <Gauge score={fit.score} tier={fit.tier} />
              <ul className="breakdown">
                {fit.breakdown.map((l) => (
                  <li key={l.criterion} title={l.detail}>
                    <span>{l.criterion}</span>
                    <span className="bar">
                      <i style={{ width: `${(l.points / l.max) * 100}%` }} />
                    </span>
                    <span className="mono">
                      {l.points}/{l.max}
                    </span>
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <Pending />
          )}
        </div>

        <div className="brief-box">
          <h3>Relationship</h3>
          {history ? (
            <>
              <span className={`rel r-${history.relationship}`}>{RELATIONSHIP[history.relationship]}</span>
              <p className="small">{history.summary}</p>
              {history.notes.length > 0 && (
                <ul className="notes">
                  {history.notes.map((n) => (
                    <li key={n}>{n}</li>
                  ))}
                </ul>
              )}
            </>
          ) : (
            <Pending />
          )}
          <h3>Contact policy</h3>
          {policy ? (
            <>
              <span className={`pol pol-${policy.status}`}>{POLICY[policy.status]}</span>
              {policy.reasons.map((r) => (
                <p key={r} className="small muted">
                  {r}
                </p>
              ))}
            </>
          ) : (
            <Pending />
          )}
        </div>
      </div>

      <div className="brief-box">
        <h3>Research</h3>
        {research ? (
          <>
            <p className="small">{research.summary}</p>
            {research.signals.length > 0 && (
              <div className="signals">
                {research.signals.map((s) => (
                  <span key={s} className="signal">
                    {s}
                  </span>
                ))}
              </div>
            )}
            <ul className="facts">
              {research.facts.map((f) => (
                <li key={f.id}>
                  <span className="mono fact-id">{f.id}</span>
                  <span>{f.text}</span>
                  <span className="fact-src mono">{f.source}</span>
                </li>
              ))}
            </ul>
          </>
        ) : (
          <Pending />
        )}
      </div>
    </div>
  );
}

function Pending() {
  return <p className="muted small pending">waiting for agent…</p>;
}

function Gauge({ score, tier }: { score: number; tier: string }) {
  const r = 34;
  const c = Math.PI * r;
  return (
    <svg className={`gauge g-${tier}`} viewBox="0 0 90 54">
      <path d={`M${45 - r},48 A${r},${r} 0 0 1 ${45 + r},48`} className="gauge-bg" />
      <path
        d={`M${45 - r},48 A${r},${r} 0 0 1 ${45 + r},48`}
        className="gauge-fg"
        strokeDasharray={`${(score / 100) * c} ${c}`}
      />
      <text x="45" y="42" className="gauge-score">
        {score}
      </text>
      <text x="45" y="53" className="gauge-tier">
        tier {tier}
      </text>
    </svg>
  );
}
