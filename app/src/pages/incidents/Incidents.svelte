<!-- Every Incident of the last 24 hours, latest first: what interrupted syncing, or could have lost changes, while
     nobody was looking. Linked from the Overview's worker card. Read-only. -->
<script lang="ts">
    import { ArrowLeft, CircleCheck } from "@lucide/svelte";
    import type { IncidentView, IncidentsView } from "../../lib/types/views";
    import { directionLabel, duration, incidentKindLabel } from "../../lib/utils/run";
    import { when } from "../../lib/utils/format";

    let { page }: { page: IncidentsView } = $props();

    const tone = (i: IncidentView) =>
        i.endedAt === null ? "border-warning-500" :
        i.kind === "checkpointDiscarded" ? "border-error-500" :
        i.kind === "started" || i.kind === "stopped" ? "border-surface-200-800" : "border-surface-300-700";
</script>

<a href="/" class="anchor mb-4 inline-flex items-center gap-1 text-sm"><ArrowLeft class="size-3" />Overview</a>

<header class="mb-6">
    <h1 class="h3">Incidents</h1>
    <p class="mt-1 text-surface-600-400">
        What interrupted syncing in the last 24 hours. Retries in one stretch of trouble count as one Incident.
    </p>
</header>

{#if page.incidents.length === 0}
    <section class="card flex max-w-3xl items-center gap-3 border border-surface-200-800 bg-surface-50-950 p-6">
        <CircleCheck class="size-6 text-success-600-400" />
        <p>Nothing interrupted syncing in the last 24 hours.</p>
    </section>
{:else}
    <ol class="max-w-4xl space-y-3">
        {#each page.incidents as incident (incident.id)}
            <li class={["card space-y-1 border-l-4 border p-4 text-sm", tone(incident)]}>
                <div class="flex flex-wrap items-baseline justify-between gap-2">
                    <p class="font-semibold">
                        {incidentKindLabel[incident.kind]}{#if incident.direction}<span class="font-normal text-surface-600-400"> · {directionLabel[incident.direction]}</span>{/if}
                        {#if incident.endedAt === null}<span class="badge preset-filled-warning-500 ml-2">Ongoing</span>{/if}
                    </p>
                    <p class="text-xs text-surface-600-400">
                        {when(incident.startedAt)}{#if duration(incident)} · {duration(incident)}{/if}{#if incident.occurrences > 1} · {incident.occurrences} times{/if}
                    </p>
                </div>
                <p class="break-words">{incident.message}</p>
            </li>
        {/each}
    </ol>
{/if}
