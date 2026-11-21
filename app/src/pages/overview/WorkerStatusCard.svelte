<!-- The worker card: what each direction of syncing is doing right now (its Run State), and what has happened
     lately. Arrives with the page (ADR 0006), then polls api/worker-status every 10 s while the tab is visible, so a
     recovery or a new problem shows without reloading. Its minimum height fits both rows, so a poll never resizes it. -->
<script lang="ts">
    import { Activity, ArrowRight, CircleAlert, TriangleAlert } from "@lucide/svelte";
    import { onMount } from "svelte";
    import { getJson } from "../../lib/api/client";
    import type { WorkerStatusView } from "../../lib/types/views";
    import { ago, bytes, directionLabel, duration, inTime, incidentKindLabel, runStateMeta, time } from "../../lib/utils/run";

    let { status = $bindable(), focus = false, class: klass = "" }:
        { status: WorkerStatusView; focus?: boolean; class?: string } = $props();

    const PollEvery = 10_000;

    // Ticks once a second so "4 min ago" and "in 12 s" stay true between polls.
    let now = $state(Date.now());
    let pollFailed = $state(false);

    async function poll() {
        const result = await getJson<WorkerStatusView>("/api/worker-status");
        pollFailed = !result.ok;
        if (result.ok) status = result.value;
    }

    onMount(() => {
        let timer: ReturnType<typeof setInterval> | null = null;
        const start = () => { timer ??= setInterval(poll, PollEvery); };
        const stop = () => { if (timer !== null) { clearInterval(timer); timer = null; } };
        const onVisibility = () => {
            if (document.hidden) {
                stop();
            } else {
                poll();
                start();
            }
        };

        const clock = setInterval(() => now = Date.now(), 1000);
        if (!document.hidden) start();
        document.addEventListener("visibilitychange", onVisibility);
        return () => {
            stop();
            clearInterval(clock);
            document.removeEventListener("visibilitychange", onVisibility);
        };
    });

    const inbound = $derived(status.inbound);
    const im = $derived(runStateMeta[status.inbound.state]);
    const writeback = $derived(status.writeback);
    const incident = $derived(status.latestIncident);
    const failures = $derived(status.inboundFailures + status.writebackFailures);
</script>

<section class={["card flex min-h-80 flex-col gap-5 border p-6", focus ? "preset-outlined-primary-500" : "border-surface-200-800 bg-surface-50-950", klass]}>
    <header class="flex flex-wrap items-baseline justify-between gap-2">
        <h2 class="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-surface-600-400">
            <Activity class="size-4" /> Worker status
        </h2>
        <p class="text-xs text-surface-600-400">
            Counts since the application started, {ago(status.startedAt, now)}
            {#if pollFailed}<span class="text-warning-700-300"> · couldn't refresh, showing the last answer</span>{/if}
        </p>
    </header>

    <!-- Inbound -->
    <div class="grid gap-x-6 gap-y-2 sm:grid-cols-[10rem_1fr]">
        <div class="space-y-1">
            <p class="font-semibold">Inbound</p>
            <p class="text-xs text-surface-600-400">Salesforce → Target Database</p>
        </div>
        <div class="space-y-2">
            <div class="flex flex-wrap items-center gap-2">
                <span class={["badge gap-1", im.badge]}><im.icon class={["size-3", inbound.state === "starting" && "animate-spin"]} />{im.label}</span>
                {#if inbound.state === "retrying" && inTime(inbound.nextAttemptAt, now)}
                    <span class="text-xs text-surface-600-400">trying again {inTime(inbound.nextAttemptAt, now)}</span>
                {/if}
                {#if inbound.state === "running" || inbound.state === "idle"}
                    <span class="text-xs text-surface-600-400">last heard from Salesforce {ago(inbound.lastHeardAt, now)}</span>
                {/if}
            </div>
            {#if inbound.stale}
                <p class="flex gap-2 text-sm text-warning-700-300">
                    <TriangleAlert class="size-4 shrink-0" />
                    Nothing heard from Salesforce for over 5 minutes, not even a keepalive. The stream may be stuck.
                </p>
            {/if}
            {#if inbound.reason && inbound.state !== "running"}
                <p class={["text-sm", inbound.state === "blocked" ? "text-error-700-300" : inbound.state === "retrying" ? "text-warning-700-300" : "text-surface-600-400"]}>{inbound.reason}</p>
            {/if}
            <p class="text-sm">
                <strong>{inbound.eventsApplied.toLocaleString()}</strong> {inbound.eventsApplied === 1 ? "change" : "changes"} applied
                {#if inbound.lastAppliedAt}<span class="text-surface-600-400"> · last {ago(inbound.lastAppliedAt, now)}</span>{/if}
                {#if status.inboundFailures > 0}
                    <span class="text-error-700-300"> · <strong>{status.inboundFailures}</strong> couldn't be applied</span>
                {/if}
            </p>
        </div>
    </div>

    <!-- Writeback, only when a Binding is Two-way -->
    {#if writeback}
        {@const wm = runStateMeta[writeback.state]}
        <div class="grid gap-x-6 gap-y-2 border-t border-surface-200-800 pt-5 sm:grid-cols-[10rem_1fr]">
            <div class="space-y-1">
                <p class="font-semibold">Writeback</p>
                <p class="text-xs text-surface-600-400">Target Database → Salesforce</p>
            </div>
            <div class="space-y-2">
                <div class="flex flex-wrap items-center gap-2">
                    <span class={["badge gap-1", wm.badge]}><wm.icon class="size-3" />{wm.label}</span>
                    {#if writeback.lagBytes !== null}
                        <span class="text-xs text-warning-700-300">{bytes(writeback.lagBytes)} behind the Target Database</span>
                    {/if}
                </div>
                {#if writeback.reason && writeback.state !== "running"}
                    <p class={["text-sm", writeback.state === "blocked" ? "text-error-700-300" : "text-warning-700-300"]}>{writeback.reason}</p>
                {/if}
                <p class="text-sm">
                    {#if writeback.lastAppliedAt}Last wrote back {ago(writeback.lastAppliedAt, now)}{:else}<span class="text-surface-600-400">Nothing written back since the application started</span>{/if}
                    {#if status.writebackFailures > 0}
                        <span class="text-error-700-300"> · <strong>{status.writebackFailures}</strong> Salesforce refused</span>
                    {/if}
                </p>
            </div>
        </div>
    {/if}

    <!-- What happened lately -->
    <footer class="mt-auto flex flex-wrap items-center justify-between gap-3 border-t border-surface-200-800 pt-4 text-sm">
        {#if incident}
            <p class="min-w-0 flex-1">
                <span class={["font-semibold", incident.endedAt === null ? "text-warning-700-300" : ""]}>
                    {incidentKindLabel[incident.kind]}{#if incident.direction} ({directionLabel[incident.direction]}){/if}
                </span>
                <span class="text-surface-600-400">
                    · {time(incident.startedAt)}{#if incident.occurrences > 1} · {incident.occurrences} times{/if}{#if duration(incident, now)} · {duration(incident, now)}{/if}
                </span>
                <span class="block truncate text-xs text-surface-600-400" title={incident.message}>{incident.message}</span>
            </p>
            <a href="/incidents" class="anchor flex shrink-0 items-center gap-1">
                {#if status.otherIncidents > 0}{status.otherIncidents} more in the last 24 h{:else}View{/if}
                <ArrowRight class="size-3" />
            </a>
        {:else}
            <p class="text-surface-600-400">No interruptions in the last 24 hours.</p>
        {/if}
        {#if failures > 0}
            <a href="/bindings" class="anchor flex w-full items-center gap-1 text-error-700-300">
                <CircleAlert class="size-3" />
                {failures} {failures === 1 ? "change needs" : "changes need"} you on the Bindings page
            </a>
        {/if}
    </footer>
</section>
