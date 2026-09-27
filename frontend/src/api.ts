import type { GraphNode, Health, LeadSummary, NewLead, PilotEvent, Workspace } from "./types";

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error ?? `${res.status} ${res.statusText}`);
  }
  return res.json() as Promise<T>;
}

export const getHealth = () => fetch("/api/health").then((r) => json<Health>(r));

export const getGraph = () => fetch("/api/graph").then((r) => json<{ nodes: GraphNode[] }>(r));

export const createWorkspace = () => fetch("/api/workspaces", { method: "POST" }).then((r) => json<Workspace>(r));

export const getWorkspace = (id: string) => fetch(`/api/workspaces/${id}`).then((r) => json<Workspace>(r));

export const addLead = (id: string, lead: NewLead) =>
  fetch(`/api/workspaces/${id}/leads`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(lead),
  }).then((r) => json<LeadSummary>(r));

export function runLead(workspaceId: string, leadId: string, onEvent: (e: PilotEvent) => void) {
  return stream(`/api/workspaces/${workspaceId}/runs`, { leadId }, onEvent);
}

export function decide(
  workspaceId: string,
  runId: string,
  decision: { approved: boolean; subject?: string; body?: string; note?: string },
  onEvent: (e: PilotEvent) => void,
) {
  return stream(`/api/workspaces/${workspaceId}/runs/${runId}/decision`, decision, onEvent);
}

/** POSTs a JSON body and parses the text/event-stream response incrementally. */
async function stream(url: string, body: unknown, onEvent: (e: PilotEvent) => void) {
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "text/event-stream" },
    body: JSON.stringify(body),
  });
  if (!res.ok || !res.body) {
    await json(res);
    return;
  }

  const reader = res.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += value.replace(/\r\n/g, "\n");

    let boundary: number;
    while ((boundary = buffer.indexOf("\n\n")) >= 0) {
      const frame = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      let type = "message";
      const data: string[] = [];
      for (const line of frame.split("\n")) {
        if (line.startsWith("event:")) type = line.slice(6).trim();
        else if (line.startsWith("data:")) data.push(line.slice(5).trimStart());
      }
      if (data.length > 0) onEvent({ type, data: JSON.parse(data.join("\n")) } as PilotEvent);
    }
  }
}
