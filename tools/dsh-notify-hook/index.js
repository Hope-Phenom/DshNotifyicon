// dsh-notify-hook: emits a structured DSH_NOTIFY line to stdout when a
// turn/end event occurs. DshNotifyicon parses these lines and shows tray
// notifications / runs user-configured external commands.
//
// Enable only when DshNotifyicon starts dsh (DSH_NOTIFY_ENABLED=1).
import process from "node:process";

export const name = "dsh-notify-hook";

function shouldEmit() {
  return process.env.DSH_NOTIFY_ENABLED === "1";
}

function emit(event) {
  if (!shouldEmit()) return;
  process.stdout.write("DSH_NOTIFY " + JSON.stringify(event) + "\n");
}

function getSessionTitle(session) {
  const events = session.events || [];
  for (let i = events.length - 1; i >= 0; i--) {
    const event = events[i];
    if (event.type === "session/title" && event.data && event.data.title) {
      return event.data.title;
    }
  }
  return "";
}

export function apply(ctx) {
  const turnStarts = new Map();

  ctx.on("session/event", (session, event) => {
    // Root sessions only unless the user explicitly wants subagents too.
    const isRoot = !session.header.parentSession;
    const includeSub = process.env.DSH_NOTIFY_INCLUDE_SUBAGENTS === "1";
    if (!isRoot && !includeSub) return;

    if (event.type === "turn/start") {
      turnStarts.set(session.id + ":" + event.data.turn, Date.now());
      return;
    }

    if (event.type !== "turn/end") return;

    const key = session.id + ":" + event.data.turn;
    const startedAt = turnStarts.get(key) || Date.now();
    turnStarts.delete(key);

    emit({
      event: "turn-end",
      sessionId: session.id,
      parentSessionId: session.header.parentSession || null,
      title: getSessionTitle(session),
      turn: event.data.turn,
      reason: event.data.reason?.kind || "unknown",
      durationMs: Math.max(0, Date.now() - startedAt)
    });
  });
}
