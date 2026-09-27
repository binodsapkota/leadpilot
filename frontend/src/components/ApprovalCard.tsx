import { useState } from "react";
import { DECISION_LABEL } from "../nodes";
import type { RunState } from "../run";

interface Props {
  run: RunState;
  busy: boolean;
  onDecide: (d: { approved: boolean; subject?: string; body?: string; note?: string }) => void;
}

export function ApprovalCard({ run, busy, onDecide }: Props) {
  const request = run.approval!.request;
  const [subject, setSubject] = useState(request.draft?.subject ?? "");
  const [body, setBody] = useState(request.draft?.body ?? "");
  const [note, setNote] = useState("");
  const [editing, setEditing] = useState(false);
  const decided = run.decision !== undefined;
  const edited = request.draft && (subject !== request.draft.subject || body !== request.draft.body);

  return (
    <section className={`card approval ${decided ? run.decision : ""}`}>
      <div className="approval-head">
        <span className="lock" aria-hidden>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <rect x="4" y="11" width="16" height="10" rx="2" />
            <path d="M8 11V7a4 4 0 0 1 8 0v4" />
          </svg>
        </span>
        <div>
          <strong>{!decided ? "Rep approval required" : run.decision === "approved" ? "Approved by rep" : "Rejected by rep"}</strong>
          <span className="muted small">
            {!decided
              ? "Workflow paused at the request port. Nothing has been written yet."
              : "The decision resumed the workflow; the CRM writer ran the changes below."}
          </span>
        </div>
      </div>

      <div className="approval-facts">
        <span className={`decision-pill d-${request.plan.decision}`}>{DECISION_LABEL[request.plan.decision]}</span>
        <span>
          {request.leadName}
          {request.company ? ` · ${request.company}` : ""}
        </span>
        <span className="muted">
          fit {request.fit.score} ({request.fit.tier})
          {request.plan.ownerName ? ` · ${request.plan.ownerName}` : ""}
        </span>
      </div>

      {request.review && !request.review.passed && (
        <p className="warn-line">The reviewer still has open issues on this draft. The CRM will refuse to queue it unless you fix them.</p>
      )}

      <h3>{decided && run.decision === "rejected" ? "Proposed (not written)" : "Will write to the CRM"}</h3>
      <ol className="proposed">
        {request.proposedChanges.map((c) => (
          <li key={c}>{c}</li>
        ))}
      </ol>

      {request.draft && !decided && (
        <>
          <div className="approval-email-head">
            <h3>Email v{request.draft.version}</h3>
            {!decided && (
              <button className="chip" onClick={() => setEditing(!editing)}>
                {editing ? "Done editing" : "Edit"}
              </button>
            )}
          </div>
          {editing && !decided ? (
            <div className="edit">
              <input value={subject} onChange={(e) => setSubject(e.target.value)} maxLength={200} />
              <textarea value={body} onChange={(e) => setBody(e.target.value)} rows={10} maxLength={4000} />
            </div>
          ) : (
            <div className="mail compact">
              <div className="mail-subject">{subject}</div>
              <pre className="mail-body">{body}</pre>
            </div>
          )}
        </>
      )}

      {!decided ? (
        <>
          <input
            className="note"
            placeholder="Note for the CRM (optional, used if you reject)"
            value={note}
            maxLength={500}
            onChange={(e) => setNote(e.target.value)}
          />
          <div className="approval-actions">
            <button
              className="approve"
              disabled={busy}
              onClick={() =>
                onDecide({ approved: true, subject: edited ? subject : undefined, body: edited ? body : undefined, note: note || undefined })
              }
            >
              {edited ? "Approve with edits" : "Approve & write to CRM"}
            </button>
            <button className="decline" disabled={busy} onClick={() => onDecide({ approved: false, note: note || undefined })}>
              Reject
            </button>
          </div>
        </>
      ) : (
        <div className={`approval-result ${run.decision}`}>
          {run.summary ? run.summary.outcome : run.decision === "approved" ? "Approved. Writing to the CRM…" : "Rejected. Flagging lead…"}
          {run.summary?.errors.map((e) => (
            <p key={e} className="form-error">
              {e}
            </p>
          ))}
        </div>
      )}
    </section>
  );
}
