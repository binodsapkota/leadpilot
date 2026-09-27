import { useEffect, useState } from "react";
import type { RunState } from "../run";

export function EmailPanel({ run }: { run?: RunState }) {
  const versions = Math.max(run?.drafts.length ?? 0, ...Object.keys(run?.streaming ?? {}).map(Number));
  const [shown, setShown] = useState(versions);

  useEffect(() => setShown(versions), [versions]);

  if (!run || versions === 0) {
    return (
      <p className="muted empty">
        {run?.plan && run.plan.decision !== "pursue"
          ? "No email for this lead: the decision was not to pursue."
          : "The copywriter drafts here once the strategist decides to pursue. The reviewer can send drafts back."}
      </p>
    );
  }

  const draft = run.drafts.find((d) => d.version === shown);
  const review = run.reviews.find((r) => r.version === shown);
  const live = run.streaming[shown] ?? "";
  const writing = !draft;

  return (
    <div className="email">
      <div className="versions">
        {Array.from({ length: versions }, (_, i) => i + 1).map((v) => {
          const r = run.reviews.find((x) => x.version === v);
          const written = run.drafts.some((d) => d.version === v);
          return (
            <button key={v} className={`version ${v === shown ? "on" : ""}`} onClick={() => setShown(v)}>
              v{v}
              <span className={`vstate ${r ? (r.passed ? "ok" : "fail") : "pending"}`}>
                {r ? (r.passed ? "passed" : "sent back") : written ? "in review" : "writing"}
              </span>
            </button>
          );
        })}
      </div>

      <div className="mail">
        {draft ? (
          <>
            <div className="mail-subject">
              <span className="muted">Subject</span> {draft.subject}
            </div>
            <pre className="mail-body">{highlight(draft.body, review?.issues.map((i) => i.excerpt ?? "") ?? [])}</pre>
          </>
        ) : (
          <pre className="mail-body">
            {live}
            {writing && <span className="caret" />}
          </pre>
        )}
      </div>

      {review && (
        <div className={`review ${review.passed ? "ok" : "fail"}`}>
          <div className="review-head">
            <strong>Reviewer</strong>
            <span>{review.passed ? "Approved for the rep" : "Sent back to copywriter"}</span>
          </div>
          <p className="small">{review.summary}</p>
          {review.issues.length > 0 && (
            <ul className="issues">
              {review.issues.map((i, n) => (
                <li key={n} className={i.severity}>
                  <span className="sev">{i.severity}</span> {i.message}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function highlight(body: string, excerpts: string[]) {
  const marks = excerpts.filter((e) => e && body.includes(e));
  if (marks.length === 0) return body;
  const parts: (string | React.ReactElement)[] = [];
  let rest = body;
  let key = 0;
  for (;;) {
    const hits = marks.map((m) => ({ m, at: rest.indexOf(m) })).filter((h) => h.at >= 0);
    if (hits.length === 0) break;
    const { m, at } = hits.reduce((a, b) => (b.at < a.at ? b : a));
    parts.push(rest.slice(0, at), <mark key={key++}>{m}</mark>);
    rest = rest.slice(at + m.length);
  }
  parts.push(rest);
  return parts;
}
