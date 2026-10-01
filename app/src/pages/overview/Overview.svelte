<!-- The Overview: the Pipeline as four Stage cards in setup order, then the reserved worker-status card. -->
<script lang="ts">
    import { ChevronRight, PartyPopper } from "@lucide/svelte";
    import type { OverviewView } from "../../lib/types/views";
    import StatusBadge from "../../lib/components/StatusBadge.svelte";
    import WorkerStatusCard from "./WorkerStatusCard.svelte";

    let { page }: { page: OverviewView } = $props();
</script>

<header class="mb-6">
    <h1 class="h3">Overview</h1>
    {#if page.allOk}
        <p class="mt-1 flex items-center gap-2 text-success-600-400"><PartyPopper class="size-4" /> Pipeline set up</p>
    {:else}
        <p class="mt-1 text-surface-600-400">Set up each Stage in order. The highlighted card is your next step.</p>
    {/if}
</header>

<div class="grid grid-cols-[1fr_auto_1fr_auto_1fr_auto_1fr] items-stretch gap-2">
    {#each page.stages as s, i (s.stage)}
        {@const next = s.stage === page.nextStep}
        <article class={[
            "card flex min-h-64 flex-col gap-3 border p-5",
            s.status === "needsAttention" ? "border-error-500 bg-error-50-950" :
            next ? "border-primary-500 ring-2 ring-primary-500/40" : "border-surface-200-800 bg-surface-50-950",
            s.status === "waiting" && "opacity-60",
            page.allOk && "shadow-none"
        ]}>
            <div class="flex items-center justify-between gap-2">
                <span class="text-xs font-semibold uppercase tracking-wide text-surface-600-400">
                    Step {s.stepNumber}{#if next} · Next step{/if}
                </span>
                <StatusBadge status={s.status} />
            </div>
            <h2 class="h5">{s.title}</h2>
            <p class="text-sm">{s.summary}</p>
            {#if s.problem}
                <div class="mt-auto space-y-2 text-sm">
                    <p><span class="font-semibold">Problem:</span> {s.problem}</p>
                    <p><span class="font-semibold">What to do:</span> {s.whatToDo}</p>
                </div>
                <a href={s.actionHref} class={["btn btn-sm self-start", next ? "preset-filled-primary-500" : s.status === "needsAttention" ? "preset-filled-error-500" : "preset-tonal"]}>
                    {s.actionLabel}
                </a>
            {/if}
        </article>
        {#if i < page.stages.length - 1}
            <div class="flex items-center text-surface-400-600"><ChevronRight class="size-6" /></div>
        {/if}
    {/each}
</div>

<WorkerStatusCard focus={page.allOk} class="mt-8" />
