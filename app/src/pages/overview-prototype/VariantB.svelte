<!-- PROTOTYPE — Variant B "Checklist": a vertical stepper; only the next step and problems expand. Worker status in a side column. -->
<script lang="ts" module>
    export const name = "Checklist";
</script>

<script lang="ts">
    import type { OverviewView } from "./views";
    import { statusMeta } from "./status";
    import StatusBadge from "./StatusBadge.svelte";
    import WorkerStatusCard from "./WorkerStatusCard.svelte";

    let { page }: { page: OverviewView } = $props();
</script>

<div class="grid grid-cols-[minmax(0,3fr)_minmax(20rem,1fr)] gap-10">
    <section>
        <h1 class="h3 mb-1">Overview</h1>
        <p class="mb-8 text-surface-600-400">
            {page.allOk ? "Pipeline set up. Every Stage is OK." : `${page.stages.filter(s => s.status === "ok").length} of 4 Stages OK`}
        </p>

        <ol class="relative">
            {#each page.stages as s, i (s.stage)}
                {@const meta = statusMeta[s.status]}
                {@const expanded = s.stage === page.nextStep || s.status === "needsAttention"}
                <li class="relative flex gap-5 pb-8">
                    {#if i < page.stages.length - 1}
                        <span class="absolute left-5 top-11 bottom-0 w-px bg-surface-300-700"></span>
                    {/if}
                    <span class={["relative z-10 grid size-10 shrink-0 place-items-center rounded-full border-2 bg-surface-50-950", meta.text,
                        s.status === "ok" ? "border-success-500" : s.status === "needsAttention" ? "border-error-500" : s.stage === page.nextStep ? "border-primary-500" : "border-surface-300-700"]}>
                        <meta.icon class="size-5" />
                    </span>
                    <div class={["flex-1 pt-1.5", s.status === "waiting" && "opacity-60"]}>
                        <div class="flex items-baseline gap-3">
                            <h2 class="h5">{s.stepNumber}. {s.title}</h2>
                            <StatusBadge status={s.status} />
                            <span class="text-sm text-surface-600-400">{s.summary}</span>
                        </div>
                        {#if expanded && s.problem}
                            <div class={["card mt-3 max-w-3xl space-y-2 border p-4 text-sm", s.status === "needsAttention" ? "border-error-500 bg-error-50-950" : "border-primary-500 bg-primary-50-950"]}>
                                <p><span class="font-semibold">Problem:</span> {s.problem}</p>
                                <p><span class="font-semibold">What to do:</span> {s.whatToDo}</p>
                                <a href={s.actionHref} class={["btn btn-sm mt-1", s.stage === page.nextStep ? "preset-filled-primary-500" : "preset-filled-error-500"]}>{s.actionLabel}</a>
                            </div>
                        {/if}
                    </div>
                </li>
            {/each}
        </ol>
    </section>

    <aside class="pt-14">
        <WorkerStatusCard focus={page.allOk} />
    </aside>
</div>
