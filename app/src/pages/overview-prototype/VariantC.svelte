<!-- PROTOTYPE — Variant C "Next step first": one hero for the next step, other problems as alerts, then a compact status table. -->
<script lang="ts" module>
    export const name = "Next step first";
</script>

<script lang="ts">
    import { ArrowRight, CircleCheck, TriangleAlert } from "@lucide/svelte";
    import type { OverviewView } from "./views";
    import StatusBadge from "./StatusBadge.svelte";
    import WorkerStatusCard from "./WorkerStatusCard.svelte";

    let { page }: { page: OverviewView } = $props();
    const next = $derived(page.stages.find(s => s.stage === page.nextStep));
    const otherProblems = $derived(page.stages.filter(s => s.status === "needsAttention" && s.stage !== page.nextStep));
</script>

<h1 class="h3 mb-6">Overview</h1>

{#if next}
    <section class={["card mb-4 max-w-5xl border-l-4 p-8", next.status === "needsAttention" ? "border-error-500 bg-error-50-950" : "border-primary-500 bg-primary-50-950"]}>
        <p class="text-xs font-semibold uppercase tracking-wide text-surface-600-400">Next step · Step {next.stepNumber} of 4</p>
        <h2 class="h3 mt-1 flex items-center gap-3">{next.title} <StatusBadge status={next.status} /></h2>
        <p class="mt-3 text-lg">{next.problem}</p>
        <p class="mt-1 text-surface-700-300">{next.whatToDo}</p>
        <a href={next.actionHref} class="btn preset-filled-primary-500 mt-5">{next.actionLabel} <ArrowRight class="size-4" /></a>
    </section>
{:else}
    <section class="card mb-4 flex max-w-5xl items-center gap-4 border border-success-500 p-6">
        <CircleCheck class="size-8 text-success-600-400" />
        <div>
            <h2 class="h4">Pipeline set up</h2>
            <p class="text-surface-600-400">All four Stages are OK.</p>
        </div>
    </section>
{/if}

{#each otherProblems as s (s.stage)}
    <aside class="card mb-3 flex max-w-5xl items-start gap-3 border border-error-500 p-4 text-sm">
        <TriangleAlert class="mt-0.5 size-5 shrink-0 text-error-600-400" />
        <div class="flex-1">
            <p><span class="font-semibold">{s.title}:</span> {s.problem}</p>
            <p class="text-surface-700-300">{s.whatToDo}</p>
        </div>
        <a href={s.actionHref} class="btn btn-sm preset-tonal-error shrink-0">{s.actionLabel}</a>
    </aside>
{/each}

<div class="mt-8 grid grid-cols-[minmax(0,3fr)_minmax(20rem,1fr)] gap-8">
    <div class="table-wrap">
        <table class="table">
            <thead>
                <tr><th>Step</th><th>Stage</th><th>Status</th><th>Where it stands</th><th></th></tr>
            </thead>
            <tbody>
                {#each page.stages as s (s.stage)}
                    <tr class={s.status === "waiting" ? "opacity-60" : ""}>
                        <td>{s.stepNumber}</td>
                        <td class="font-semibold">{s.title}</td>
                        <td><StatusBadge status={s.status} /></td>
                        <td>{s.summary}</td>
                        <td class="text-right">
                            {#if s.actionHref}<a class="anchor" href={s.actionHref}>Open</a>{/if}
                        </td>
                    </tr>
                {/each}
            </tbody>
        </table>
    </div>
    <WorkerStatusCard focus={page.allOk} />
</div>
