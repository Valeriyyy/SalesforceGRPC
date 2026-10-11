<!-- Until there is a Primary Channel to read Entities from and a Target Connection whose tables can be read, the
     Bindings pages offer nothing but the steps still to do, each linking to where it is done. -->
<script lang="ts">
    import { ArrowRight, Hourglass } from "@lucide/svelte";
    import type { SetupStepView } from "../../lib/types/views";

    let { steps }: { steps: SetupStepView[] } = $props();
</script>

<section class="card flex max-w-3xl items-start gap-4 border border-surface-200-800 bg-surface-50-950 p-6">
    <Hourglass class="size-8 shrink-0 text-surface-600-400" />
    <div class="space-y-3">
        <div class="space-y-1">
            <h2 class="h5">Waiting on {steps.map(s => s.title).join(" and ")}</h2>
            <p class="text-sm text-surface-600-400">A Binding sends one Entity on the Primary Channel to a table in the Target Database, so both have to be set up first.</p>
        </div>
        <ol class="space-y-3">
            {#each steps as step}
                <li class="text-sm">
                    <p class="font-semibold">{step.title}</p>
                    <p class="text-surface-600-400">{step.problem}</p>
                    <a href={step.href} class="anchor inline-flex items-center gap-1">{step.actionLabel}<ArrowRight class="size-3" /></a>
                </li>
            {/each}
        </ol>
    </div>
</section>
