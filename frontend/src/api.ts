import type { AgentInfo, DemoCredential, DeskEvent, Health, Scenario, SessionState } from "./types";

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error ?? `${res.status} ${res.statusText}`);
  }
  return res.json() as Promise<T>;
}

export const getHealth = () => fetch("/api/health").then((r) => json<Health>(r));

export const getAgents = () => fetch("/api/agents").then((r) => json<AgentInfo[]>(r));

export const getScenarios = () =>
  fetch("/api/scenarios").then((r) => json<{ scenarios: Scenario[]; credentials: DemoCredential[] }>(r));

export const createSession = () => fetch("/api/sessions", { method: "POST" }).then((r) => json<SessionState>(r));

export function sendMessage(sessionId: string, text: string, onEvent: (e: DeskEvent) => void) {
  return stream(`/api/sessions/${sessionId}/messages`, { text }, onEvent);
}

export function resolveApproval(sessionId: string, requestId: string, approved: boolean, onEvent: (e: DeskEvent) => void) {
  return stream(`/api/sessions/${sessionId}/approval`, { requestId, approved }, onEvent);
}

/** POSTs a JSON body and parses the text/event-stream response incrementally. */
async function stream(url: string, body: unknown, onEvent: (e: DeskEvent) => void) {
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
      if (data.length > 0) onEvent({ type, data: JSON.parse(data.join("\n")) } as DeskEvent);
    }
  }
}
