<!-- What the last action did, shown once after the page reloads: a Resync's report, an adopted Channel, a deletion. -->
<script lang="ts">
    import { CircleCheck, TriangleAlert, X } from "@lucide/svelte";
    import type { ChannelsNotice } from "../../lib/types/views";

    let { notice }: { notice: ChannelsNotice | null } = $props();
    let dismissed = $state(false);

    const report = $derived(notice?.resync ?? null);
</script>

{#if notice && !dismissed}
    <div role={notice.kind === "error" ? "alert" : "status"}
        class={["card relative mb-6 flex gap-3 border p-4 pr-12 text-sm",
            notice.kind === "error" ? "border-warning-500 bg-warning-50-950" : "border-success-500 bg-success-50-950"]}>
        {#if notice.kind === "error"}
            <TriangleAlert class="size-5 shrink-0 text-warning-600-400" />
        {:else}
            <CircleCheck class="size-5 shrink-0 text-success-600-400" />
        {/if}
        <div class="space-y-2">
            <p class="font-semibold">{notice.message}</p>
            {#if report?.hasChanges}
                <ul class="list-disc space-y-0.5 pl-5">
                    {#each report.channelsAdded as name}<li><span class="font-mono">{name}</span> added</li>{/each}
                    {#each report.channelsRemoved as name}<li><span class="font-mono">{name}</span> removed</li>{/each}
                    {#each report.channelsRelabelled as name}<li><span class="font-mono">{name}</span> relabelled</li>{/each}
                    {#each report.membersAdded as m}<li>{m.selectedEntity} added to <span class="font-mono">{m.channel}</span></li>{/each}
                    {#each report.membersRemoved as m}
                        <li>
                            {m.selectedEntity} removed from <span class="font-mono">{m.channel}</span>
                            {#if m.bindingSetInactive} — Binding to <span class="font-mono">{m.bindingSetInactive}</span> set Inactive{/if}
                        </li>
                    {/each}
                </ul>
            {/if}
        </div>
        <button type="button" class="btn-icon btn-icon-sm absolute right-2 top-2 hover:preset-tonal"
            onclick={() => dismissed = true} aria-label="Dismiss"><X class="size-4" /></button>
    </div>
{/if}
