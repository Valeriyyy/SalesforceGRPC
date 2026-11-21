// How Run States and Incidents look, wherever they are shown (the Overview's worker card and the Incidents page).
import { CircleAlert, CircleCheck, CirclePause, LoaderCircle, RefreshCw } from "@lucide/svelte";
import type { IncidentKind, IncidentView, RunDirection, RunStateView, WorkerStatusView } from "../types/views";

export const runStateMeta = {
    starting: { label: "Starting", icon: LoaderCircle, badge: "preset-tonal-surface" },
    idle: { label: "Idle", icon: CirclePause, badge: "preset-tonal-surface" },
    blocked: { label: "Blocked", icon: CircleAlert, badge: "preset-filled-error-500" },
    running: { label: "Running", icon: CircleCheck, badge: "preset-filled-success-500" },
    retrying: { label: "Retrying", icon: RefreshCw, badge: "preset-filled-warning-500" }
} satisfies Record<RunStateView, unknown>;

export const directionLabel = {
    inbound: "Inbound",
    writeback: "Writeback"
} satisfies Record<RunDirection, string>;

export const incidentKindLabel = {
    retrying: "Interrupted",
    blocked: "Stopped",
    checkpointDiscarded: "Changes lost",
    started: "Started",
    stopped: "Stopped cleanly"
} satisfies Record<IncidentKind, string>;

/** Mirrors OverviewView.NotSyncing on the server, so the header follows the card as it polls. */
export const notSyncing = (status: WorkerStatusView) =>
    [status.inbound.state, status.writeback?.state].some(s => s === "blocked" || s === "retrying");

/** "just now", "4 min ago", "3 h ago", "2 d ago". */
export function ago(iso: string | null | undefined, now: number = Date.now()): string {
    if (!iso) return "never";
    const seconds = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
    if (seconds < 45) return "just now";
    const minutes = Math.round(seconds / 60);
    if (minutes < 60) return `${minutes} min ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 48) return `${hours} h ago`;
    return `${Math.round(hours / 24)} d ago`;
}

/** "in 12 s", "in 3 min", or "now" once it is due. */
export function inTime(iso: string | null | undefined, now: number = Date.now()): string | null {
    if (!iso) return null;
    const seconds = Math.round((new Date(iso).getTime() - now) / 1000);
    if (seconds <= 0) return "now";
    return seconds < 90 ? `in ${seconds} s` : `in ${Math.round(seconds / 60)} min`;
}

export const time = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" });

/** How long an Incident lasted, or "ongoing". Instant ones (a start, a stop) have no duration. */
export function duration(incident: IncidentView, now: number = Date.now()): string | null {
    const start = new Date(incident.startedAt).getTime();
    const end = incident.endedAt ? new Date(incident.endedAt).getTime() : null;
    if (end === null) return `ongoing for ${span(now - start)}`;
    return end - start < 1000 ? null : span(end - start);
}

function span(ms: number): string {
    const minutes = Math.round(ms / 60000);
    if (minutes < 1) return "under a minute";
    if (minutes < 60) return `${minutes} min`;
    const hours = Math.floor(minutes / 60);
    return `${hours} h ${minutes % 60} min`;
}

export function bytes(n: number): string {
    const units = ["B", "KB", "MB", "GB", "TB"];
    let i = 0;
    let v = n;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return `${v.toFixed(v < 10 && i > 0 ? 1 : 0)} ${units[i]}`;
}
